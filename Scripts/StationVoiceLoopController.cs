using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class StationVoiceLoopController : MonoBehaviour
{
    private enum LoopState
    {
        Idle,
        Listening,
        WaitingRecognition,
        PlayingAnswer
    }

    [Header("References")]
    [SerializeField] private DoubaoSpeechToText doubaoSpeechToText;
    [SerializeField] private AudioRetrieval_JS audioRetrieval;
    [SerializeField] private Button holdButton;

    [Header("UI")]
    [SerializeField] private Text statusTextPrefab;      // 每轮对话文本预制体
    [SerializeField] private Transform statusTextParent; // 预制体父节点（Content）
    [SerializeField] private Text stateText;             // 可选：状态提示（待机/录音中等）

    [Header("Pre Prompt Audio")]
    [SerializeField] private bool playPromptBeforeEachRound = true;
    [SerializeField] private AudioSource promptAudioSource;
    [SerializeField] private AudioClip promptAudioClip; // 直接拖入前置音频

    [Header("Options")]
    [SerializeField] private bool stopCurrentAnswerWhenReleased = false;

    [Header("Debug")]
    [SerializeField] private bool enableTriggerLogs = true;

    private bool _isHolding;
    private bool _buttonEventsRegistered;
    private bool _isPreparingRound;
    private Coroutine _prepareRoundCoroutine;
    private Coroutine _waitPlayEndCoroutine;
    private bool _pendingStartAfterPlayback;

    private LoopState _state = LoopState.Idle;
    private Text _currentRoundText;

    // 本轮识别兜底与去重
    private string _latestRoundText = string.Empty;
    private bool _recognitionHandledThisRound;

    private void Awake()
    {
        if (doubaoSpeechToText == null)
        {
            doubaoSpeechToText = GetComponent<DoubaoSpeechToText>();
        }

        if (audioRetrieval == null)
        {
            audioRetrieval = GetComponent<AudioRetrieval_JS>();
        }

        RegisterHoldButtonEvents();
        SetState("待机");
    }

    private void OnEnable()
    {
        if (doubaoSpeechToText != null)
        {
            doubaoSpeechToText.OnPartialResult += HandlePartialResult;
            doubaoSpeechToText.OnRecognitionCompleted += HandleRecognitionCompleted;
            doubaoSpeechToText.OnStatusUpdated += HandleStatusUpdated;
            doubaoSpeechToText.OnError += HandleError;
        }

        if (audioRetrieval != null)
        {
            audioRetrieval.OnAudioPlayCompleted += HandleAudioPlayCompleted;
        }
    }

    private void OnDisable()
    {
        if (doubaoSpeechToText != null)
        {
            doubaoSpeechToText.OnPartialResult -= HandlePartialResult;
            doubaoSpeechToText.OnRecognitionCompleted -= HandleRecognitionCompleted;
            doubaoSpeechToText.OnStatusUpdated -= HandleStatusUpdated;
            doubaoSpeechToText.OnError -= HandleError;
        }

        if (audioRetrieval != null)
        {
            audioRetrieval.OnAudioPlayCompleted -= HandleAudioPlayCompleted;
        }

        _isHolding = false;
        _state = LoopState.Idle;
        CancelPrepareRound();
    }

    public void BeginHold()
    {
        if (_isHolding)
        {
            return;
        }

        _isHolding = true;
        SyncCommStandState(true);

        if (_state == LoopState.Idle)
        {
            StartNextRound();
        }

        LogTrigger("BeginHold");
    }

    public void EndHold()
    {
        if (!_isHolding)
        {
            return;
        }

        _isHolding = false;
        SyncCommStandState(false);
        CancelPrepareRound();

        if (_state == LoopState.Listening || _state == LoopState.WaitingRecognition)
        {
            _state = LoopState.WaitingRecognition;
            SetState("已停止录音，等待识别...");
            doubaoSpeechToText?.StopRecording();
        }
        else if (_state == LoopState.PlayingAnswer)
        {
            // 播放中不触发StopRecording，避免打断当前播放链路
            SetState("已离开装置，等待当前播放结束");
        }
    }

    private void StartNextRound()
    {
        LogTrigger("StartNextRound");

        if (!_isHolding)
        {
            return;
        }

        if (doubaoSpeechToText == null || audioRetrieval == null)
        {
            Debug.LogError("StationVoiceLoopController 缺少必要引用");
            return;
        }

        if (_isPreparingRound)
        {
            return;
        }

        if (_state == LoopState.Listening || _state == LoopState.WaitingRecognition)
        {
            return;
        }

        if (audioRetrieval.IsPlayingAudio)
        {
            // 播放期间先预热录音，不上传
            doubaoSpeechToText.StartWarmupRecording();

            SetState("回答播放中，等待后开始下一轮...");
            QueueStartAfterPlayback();
            return;
        }

        _prepareRoundCoroutine = StartCoroutine(PrepareRoundAndStart());
    }

    private IEnumerator PrepareRoundAndStart()
    {
        _isPreparingRound = true;

        _latestRoundText = string.Empty;
        _recognitionHandledThisRound = false;

        // 前置语音播放时预热录音，不上传
        doubaoSpeechToText.StartWarmupRecording();

        CreateRoundTextItem();
        UpdateCurrentRoundText("（等待说话）");

        if (playPromptBeforeEachRound && promptAudioSource != null && promptAudioClip != null)
        {
            LogTrigger("PlayPrompt");

            SetState("前置语音播放中...");
            promptAudioSource.Stop();
            promptAudioSource.clip = promptAudioClip;
            promptAudioSource.loop = false;
            promptAudioSource.Play();

            while (_isHolding && promptAudioSource.isPlaying)
            {
                yield return null;
            }
        }

        if (!_isHolding)
        {
            _isPreparingRound = false;
            _prepareRoundCoroutine = null;
            yield break;
        }

        if (audioRetrieval != null && audioRetrieval.IsPlayingAudio)
        {
            _isPreparingRound = false;
            _prepareRoundCoroutine = null;
            SetState("回答播放中，等待后开始下一轮...");
            QueueStartAfterPlayback();
            yield break;
        }

        audioRetrieval.ClearPreMatch();
        _state = LoopState.Listening;
        SetState("开始录音...");

        // 这里才真正开启上传识别
        doubaoSpeechToText.StartRecording();

        _isPreparingRound = false;
        _prepareRoundCoroutine = null;
    }

    private void QueueStartAfterPlayback()
    {
        _pendingStartAfterPlayback = true;

        if (_waitPlayEndCoroutine != null)
        {
            return;
        }

        _waitPlayEndCoroutine = StartCoroutine(WaitPlaybackEndAndStartNextRound());
    }

    private IEnumerator WaitPlaybackEndAndStartNextRound()
    {
        while (_isHolding && audioRetrieval != null && audioRetrieval.IsPlayingAudio)
        {
            yield return null;
        }

        _waitPlayEndCoroutine = null;

        if (_isHolding && _pendingStartAfterPlayback)
        {
            _pendingStartAfterPlayback = false;
            StartNextRound();
        }
    }

    private void CancelPrepareRound()
    {
        bool wasPreparingRound = _isPreparingRound;
        _isPreparingRound = false;

        if (_prepareRoundCoroutine != null)
        {
            StopCoroutine(_prepareRoundCoroutine);
            _prepareRoundCoroutine = null;
        }

        // 仅在准备轮次阶段停止前置音频，避免误停回答音频
        if (wasPreparingRound && promptAudioSource != null && promptAudioSource.isPlaying)
        {
            promptAudioSource.Stop();
        }

        if (_waitPlayEndCoroutine != null)
        {
            StopCoroutine(_waitPlayEndCoroutine);
            _waitPlayEndCoroutine = null;
        }

        _pendingStartAfterPlayback = false;
    }

    private void HandlePartialResult(string text)
    {
        if (!_isHolding)
        {
            return;
        }

        if (_state != LoopState.Listening && _state != LoopState.WaitingRecognition)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _latestRoundText = text;

        LogTrigger($"Partial: {text}");
        EnsureRoundTextItem();
        audioRetrieval.PreMatchByInput(text);
        UpdateCurrentRoundText(text);
        SetState("识别中...");
    }

    private void HandleRecognitionCompleted(string finalText)
    {
        if (_recognitionHandledThisRound)
        {
            return;
        }

        _recognitionHandledThisRound = true;
        EnsureRoundTextItem();

        if (string.IsNullOrWhiteSpace(finalText))
        {
            finalText = _latestRoundText;
        }

        if (string.IsNullOrWhiteSpace(finalText))
        {
            UpdateCurrentRoundText("（未识别到语音）");
            _state = LoopState.Idle;
            SetState(_isHolding ? "未识别到语音，准备下一轮..." : "待机");

            if (_isHolding)
            {
                StartNextRound();
            }

            return;
        }

        UpdateCurrentRoundText(finalText);
        LogTrigger($"Final: {finalText}");

        _state = LoopState.PlayingAnswer;
        SetState("匹配并播放...");

        var matchResult = audioRetrieval.MatchByInput(finalText);
        UpdateCurrentRoundMatchInfo(matchResult);
    }

    private void UpdateCurrentRoundMatchInfo(AudioRetrieval_JS.MatchResult matchResult)
    {
        if (_currentRoundText == null || matchResult == null)
        {
            return;
        }

        if (matchResult.isMatched)
        {
            _currentRoundText.text =
                $"{_currentRoundText.text}\n" +
                $"匹配成功：{matchResult.questionText}\n" +
                $"音频名：{matchResult.audioName}\n" +
                $"匹配度：{matchResult.score:F4}";
        }
        else
        {
            _currentRoundText.text =
                $"{_currentRoundText.text}\n" +
                $"匹配成功：兜底回复\n" +
                $"音频名：{matchResult.audioName}\n" +
                $"匹配度：{matchResult.score:F4}";
        }
    }

    private void HandleAudioPlayCompleted()
    {
        if (_waitPlayEndCoroutine != null)
        {
            StopCoroutine(_waitPlayEndCoroutine);
            _waitPlayEndCoroutine = null;
        }
        _pendingStartAfterPlayback = false;

        _state = LoopState.Idle;

        if (_isHolding)
        {
            SetState("回答完成，开始下一轮...");
            StartNextRound();
        }
        else
        {
            SetState("待机");
        }
    }

    private void HandleStatusUpdated(string message)
    {
        if (message == "检测到录音停止" || message == "已发送最后一包音频")
        {
            if (_state == LoopState.Listening)
            {
                _state = LoopState.WaitingRecognition;
            }
        }

        if (_state != LoopState.PlayingAnswer)
        {
            SetState(message);
        }
    }

    private void HandleError(string error)
    {
        _state = LoopState.Idle;
        SetState("错误：" + error);
    }

    private void CreateRoundTextItem()
    {
        if (statusTextPrefab == null || statusTextParent == null)
        {
            return;
        }

        _currentRoundText = Instantiate(statusTextPrefab, statusTextParent);
        _currentRoundText.text = "你：";
    }

    private void EnsureRoundTextItem()
    {
        if (_currentRoundText == null)
        {
            CreateRoundTextItem();
        }
    }

    private void UpdateCurrentRoundText(string text)
    {
        if (_currentRoundText == null)
        {
            return;
        }

        _currentRoundText.text = $"你：{text}";
    }

    private void RegisterHoldButtonEvents()
    {
        if (_buttonEventsRegistered || holdButton == null)
        {
            return;
        }

        var trigger = holdButton.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = holdButton.gameObject.AddComponent<EventTrigger>();
        }

        if (trigger.triggers == null)
        {
            trigger.triggers = new List<EventTrigger.Entry>();
        }

        AddTrigger(trigger, EventTriggerType.PointerDown, _ => BeginHold());
        AddTrigger(trigger, EventTriggerType.PointerUp, _ => EndHold());

        _buttonEventsRegistered = true;
    }

    private void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action<BaseEventData> callback)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(new UnityEngine.Events.UnityAction<BaseEventData>(callback));
        trigger.triggers.Add(entry);
    }

    private void SetState(string message)
    {
        if (stateText != null)
        {
            stateText.text = message;
        }
    }

    private void LogTrigger(string msg)
    {
        if (!enableTriggerLogs)
        {
            return;
        }

        Debug.Log($"[StationVoiceLoopController] {msg}");
    }

    private void SyncCommStandState(bool isStanding)
    {
        if (Comm.instance == null)
        {
            return;
        }

        if (Comm.instance.anjian != null && Comm.instance.anjian.Length > 0)
        {
            Comm.instance.anjian[0] = isStanding;
        }

        if (Comm.instance.deng != null && Comm.instance.deng.Length > 0)
        {
            Comm.instance.deng[0] = !isStanding;
        }
    }
}