using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using WebSocketSharp;

public class DoubaoSpeechToText : MonoBehaviour
{
    [Header("Doubao Config")]
    public string WebSocketUrl = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async";
    public string AppKey = "";
    public string AccessKey = "";
    public string ResourceId = "volc.seedasr.sauc.duration";

    [Header("Audio Config")]
    public VoiceProcessor VoiceProcessor;
    public int SampleRate = 16000;
    public int Bits = 16;
    public int Channel = 1;
    public string Language = "zh-CN";

    [Header("Request Config")]
    public bool EnableItn = true;
    public bool EnablePunc = true;
    public bool EnableDdc = false;
    public bool ShowUtterances = true;
    public int EndWindowSize = 600;
    public int ForceToSpeechTime = 1000;
    public string ResultType = "full";

    [Header("Transport KeepAlive")]
    [SerializeField] private float keepAliveIntervalSeconds = 2.0f;
    [SerializeField] private int keepAliveSamples = 3200;

    [Header("Fail-safe")]
    [SerializeField] private float finalResultFallbackTimeoutSeconds = 0.5f;
    [SerializeField] private bool disableLocalVadForUploadDebug = false;

    [Header("Debug")]
    [SerializeField] private bool enableUploadDebugLog = true;

    [Header("State")]
    public bool AutoReconnect = false;

    public bool IsConnected => _ws != null && _ws.ReadyState == WebSocketState.Open;
    public bool IsRecording => VoiceProcessor != null && VoiceProcessor.IsRecording;

    public event Action<string> OnStatusUpdated;
    public event Action<string> OnPartialResult;
    public event Action<string> OnFinalResult;
    public event Action<string> OnRecognitionCompleted;
    public event Action<string> OnError;

    private WebSocket _ws;
    private bool _sessionStarted;
    private bool _finalPacketSent;
    private bool _recognitionCompletedRaised;
    private readonly object _sendLock = new object();

    private readonly Queue<Action> _mainThreadActions = new Queue<Action>();
    private readonly object _mainThreadActionLock = new object();
    private int _mainThreadId;

    private string _lastText = "";
    private string _lastDefiniteText = "";
    private string _deviceUniqueId;

    private double _lastAudioPacketSendTimeSec;
    private byte[] _silentKeepAlivePcmBytes;

    private int _sentAudioPacketCount;
    private bool _loggedFirstAudioPacket;
    private bool _loggedFirstServerText;
    private bool _loggedUnexpectedServerMessageType;

    private Coroutine _waitFinalFallbackCoroutine;

    private static readonly Stopwatch _clock = Stopwatch.StartNew();

    private void Awake()
    {
        _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

        if (VoiceProcessor == null)
        {
            UnityEngine.Debug.LogError("DoubaoSpeechToText: VoiceProcessor 未赋值");
        }

        _deviceUniqueId = SystemInfo.deviceUniqueIdentifier;
    }

    private void OnEnable()
    {
        if (VoiceProcessor != null)
        {
            VoiceProcessor.OnFrameCaptured += HandleFrameCaptured;
            VoiceProcessor.OnRecordingStop += HandleRecordingStop;
            VoiceProcessor.OnRecordingStart += HandleRecordingStart;
            VoiceProcessor.OnNoSpeechTimeout += HandleNoSpeechTimeout;
            VoiceProcessor.OnSilenceTimeout += HandleSilenceTimeout;
        }
    }

    private void OnDisable()
    {
        if (VoiceProcessor != null)
        {
            VoiceProcessor.OnFrameCaptured -= HandleFrameCaptured;
            VoiceProcessor.OnRecordingStop -= HandleRecordingStop;
            VoiceProcessor.OnRecordingStart -= HandleRecordingStart;
            VoiceProcessor.OnNoSpeechTimeout -= HandleNoSpeechTimeout;
            VoiceProcessor.OnSilenceTimeout -= HandleSilenceTimeout;
        }

        Disconnect();
    }

    private void Update()
    {
        FlushMainThreadActions();
        TrySendKeepAlivePacket();
    }

    /// <summary>
    /// 仅预热麦克风：开开录音但不建 WebSocket、不静音检测、不上传。
    /// </summary>
    public void StartWarmupRecording()
    {
        if (VoiceProcessor == null) return;

        // 开启麦克风（如果没开启）并关闭VAD静音检测
        VoiceProcessor.StartRecording(SampleRate, 3200, true);

        ReportStatus("开启麦克风常驻，预热阶段不上传");
    }

    /// <summary>
    /// 当玩家该说话时，调用此方法无缝开启VAD静音检测和WebSocket上传！
    /// </summary>
    public void StartRecording()
    {
        if (VoiceProcessor == null) return;

        // 已在正式上传会话中，直接返回
        if (IsRecording && _sessionStarted) return;

        // 重新设置VAD参数 (由于VoiceProcessor重构了底层，它不会打断目前已经开着的麦克风声音)
        VoiceProcessor.StartRecording(SampleRate, 3200, !disableLocalVadForUploadDebug);

        _lastText = "";
        _lastDefiniteText = "";
        _finalPacketSent = false;
        _recognitionCompletedRaised = false;
        _sessionStarted = false;

        _sentAudioPacketCount = 0;
        _loggedFirstAudioPacket = false;
        _loggedFirstServerText = false;
        _loggedUnexpectedServerMessageType = false;

        int safeSamples = Mathf.Max(160, keepAliveSamples);
        _silentKeepAlivePcmBytes = new byte[safeSamples * 2];
        _lastAudioPacketSendTimeSec = GetNowSec();

        CancelWaitFinalFallback();
        ConnectAndStartSession();
    }

    public void StopRecording()
    {
        try
        {
            if (VoiceProcessor != null && VoiceProcessor.IsRecording)
            {
                // 松开按钮，真正彻底关闭麦克风
                VoiceProcessor.StopRecording();
            }
            SendLastPacketIfNeeded();
        }
        catch (Exception ex)
        {
            ReportError("StopRecording 失败: " + ex);
        }
    }

    private void ConnectAndStartSession()
    {
        if (string.IsNullOrWhiteSpace(AppKey) || string.IsNullOrWhiteSpace(AccessKey))
        {
            ReportError("AppKey 或 AccessKey 未配置");
            return;
        }

        string connectId = Guid.NewGuid().ToString();

        _ws = new WebSocket(WebSocketUrl);
        _ws.SetUserHeader("X-Api-App-Key", AppKey);
        _ws.SetUserHeader("X-Api-Access-Key", AccessKey);
        _ws.SetUserHeader("X-Api-Resource-Id", ResourceId);
        _ws.SetUserHeader("X-Api-Connect-Id", connectId);

        _ws.OnOpen += (sender, e) =>
        {
            try
            {
                ReportStatus("豆包 WebSocket 已连接，开始识别上传");
                SendFullClientRequest();

                // 保底措施，如果麦克风刚不小心关了强制拉起来
                RunOnMainThread(() => VoiceProcessor?.StartRecording(SampleRate, 3200, !disableLocalVadForUploadDebug));
            }
            catch (Exception ex)
            {
                ReportError("OnOpen 内部异常: " + ex);
            }
        };

        _ws.OnMessage += (sender, e) =>
        {
            try
            {
                if (e.IsText && !string.IsNullOrWhiteSpace(e.Data))
                {
                    HandleServerTextJson(e.Data, false);
                    return;
                }

                if (e.RawData != null && e.RawData.Length > 0)
                {
                    HandleServerBinaryMessage(e.RawData);
                }
            }
            catch (Exception ex)
            {
                ReportError("解析服务端消息失败: " + ex);
            }
        };

        _ws.OnError += (sender, e) =>
        {
            ReportError("WebSocket 错误: " + e.Message);
            if (e.Exception != null)
            {
                ReportError("WebSocket 异常详情: " + e.Exception);
            }
        };

        _ws.OnClose += (sender, e) =>
        {
            ReportStatus($"WebSocket 已关闭: code={e.Code}, reason={e.Reason}");
        };

        _ws.ConnectAsync();
    }

    private void SendFullClientRequest()
    {
        string requestJson = BuildRequestJson();
        byte[] packet = DoubaoProtocol.BuildFullClientRequest(requestJson);

        lock (_sendLock)
        {
            _ws.Send(packet);
        }

        _sessionStarted = true;
        _lastAudioPacketSendTimeSec = GetNowSec();
    }

    private string BuildRequestJson()
    {
        StringBuilder sb = new StringBuilder();

        sb.Append("{");

        sb.Append("\"user\":{");
        sb.AppendFormat("\"uid\":\"{0}\"", EscapeJson(_deviceUniqueId));
        sb.Append("},");

        sb.Append("\"audio\":{");
        sb.Append("\"format\":\"pcm\",");
        sb.Append("\"codec\":\"raw\",");
        sb.AppendFormat("\"rate\":{0},", SampleRate);
        sb.AppendFormat("\"bits\":{0},", Bits);
        sb.AppendFormat("\"channel\":{0},", Channel);
        sb.AppendFormat("\"language\":\"{0}\"", EscapeJson(Language));
        sb.Append("},");

        sb.Append("\"request\":{");
        sb.Append("\"model_name\":\"bigmodel\",");
        sb.AppendFormat("\"enable_itn\":{0},", EnableItn.ToString().ToLower());
        sb.AppendFormat("\"enable_punc\":{0},", EnablePunc.ToString().ToLower());
        sb.AppendFormat("\"enable_ddc\":{0},", EnableDdc.ToString().ToLower());
        sb.AppendFormat("\"show_utterances\":{0},", ShowUtterances.ToString().ToLower());
        sb.AppendFormat("\"result_type\":\"{0}\",", EscapeJson(ResultType));
        sb.AppendFormat("\"end_window_size\":{0},", EndWindowSize);
        sb.AppendFormat("\"force_to_speech_time\":{0}", ForceToSpeechTime);
        sb.Append("}");

        sb.Append("}");

        return sb.ToString();
    }

    private void HandleFrameCaptured(short[] pcmSamples)
    {
        // 加判断：_finalPacketSent 发完后丢弃包数据
        if (!_sessionStarted || !IsConnected || _finalPacketSent || pcmSamples == null || pcmSamples.Length == 0)
        {
            return;
        }

        byte[] pcmBytes = ShortsToBytes(pcmSamples);
        byte[] packet = DoubaoProtocol.BuildAudioOnlyRequest(pcmBytes, false);

        try
        {
            lock (_sendLock)
            {
                _ws.Send(packet);
            }

            _sentAudioPacketCount++;
            if (enableUploadDebugLog && !_loggedFirstAudioPacket)
            {
                _loggedFirstAudioPacket = true;
                UnityEngine.Debug.Log("[豆包上传调试] 第一个音频包已发送");
            }

            _lastAudioPacketSendTimeSec = GetNowSec();
        }
        catch (Exception e)
        {
            ReportError($"发送音频数据包失败: {e.Message}");
        }
    }

    private void TrySendKeepAlivePacket()
    {
        if (!_sessionStarted || _finalPacketSent || !IsConnected)
        {
            return;
        }

        if (!IsRecording || keepAliveIntervalSeconds <= 0f)
        {
            return;
        }

        double now = GetNowSec();
        if (now - _lastAudioPacketSendTimeSec < keepAliveIntervalSeconds)
        {
            return;
        }

        try
        {
            byte[] packet = DoubaoProtocol.BuildAudioOnlyRequest(_silentKeepAlivePcmBytes, false);
            lock (_sendLock)
            {
                _ws.Send(packet);
            }

            _lastAudioPacketSendTimeSec = now;
        }
        catch (Exception e)
        {
            ReportError($"发送保活音频包失败: {e.Message}");
        }
    }

    private void HandleRecordingStart()
    {
        // 麦克风物理打开
    }

    private void HandleRecordingStop()
    {
        ReportStatus("检测到麦克风物理中止");
        SendLastPacketIfNeeded();
    }

    // 核心更改点：静音检测到后，主动切断当前流程，发送尾包！
    private void HandleNoSpeechTimeout()
    {
        ReportStatus("长时间未说话，结束本轮上传并转交匹配...");
        SendLastPacketIfNeeded();
    }

    // 核心更改点：静音检测到后，主动切断当前流程，发送尾包！
    private void HandleSilenceTimeout()
    {
        ReportStatus("玩家说话完毕，结束上传并匹配...");
        SendLastPacketIfNeeded();
    }

    private void SendLastPacketIfNeeded()
    {
        if (_finalPacketSent || !IsConnected || !_sessionStarted)
        {
            return;
        }

        _finalPacketSent = true;

        try
        {
            byte[] lastPacket = DoubaoProtocol.BuildAudioOnlyRequest(Array.Empty<byte>(), true);
            lock (_sendLock)
            {
                _ws.Send(lastPacket);
            }

            _lastAudioPacketSendTimeSec = GetNowSec();
            ReportStatus("已发送最后一包音频，断开上传");

            if (enableUploadDebugLog)
            {
                UnityEngine.Debug.Log($"[豆包上传调试] 已发送最后一包，累计音频包数={_sentAudioPacketCount}");
            }

            StartWaitFinalFallback();
        }
        catch (Exception e)
        {
            ReportError("发送最后一包失败: " + e);
        }
    }

    private void HandleServerBinaryMessage(byte[] rawData)
    {
        string asText = Encoding.UTF8.GetString(rawData);
        if (TryExtractFirstJsonObject(asText, out _))
        {
            HandleServerTextJson(asText, false);
            return;
        }

        var parsed = DoubaoProtocol.ParseServerMessage(rawData);
        if (parsed == null)
        {
            return;
        }

        CancelWaitFinalFallback();

        if (parsed.MessageType == (int)DoubaoProtocol.MessageType.ErrorResponse)
        {
            if (parsed.ErrorCode == 45000081)
            {
                string finalTextOnTimeout = !string.IsNullOrWhiteSpace(_lastDefiniteText) ? _lastDefiniteText : _lastText;
                RunOnMainThread(() => CompleteRecognitionOnMainThread(finalTextOnTimeout));
                return;
            }

            ReportError($"豆包错误码: {parsed.ErrorCode}, 消息: {parsed.PayloadJson}");
            return;
        }

        if (parsed.MessageType != (int)DoubaoProtocol.MessageType.FullServerResponse)
        {
            return;
        }

        bool isLastServerPacket = parsed.Flags == 0x3 || parsed.Flags == 0x2;

        if (!TryExtractFirstJsonObject(parsed.PayloadJson, out var payloadJson))
        {
            if (isLastServerPacket)
            {
                string finalTextIfEmptyPayload = !string.IsNullOrWhiteSpace(_lastDefiniteText) ? _lastDefiniteText : _lastText;
                RunOnMainThread(() => CompleteRecognitionOnMainThread(finalTextIfEmptyPayload));
            }
            return;
        }

        DoubaoServerResponse response;
        try
        {
            response = JsonUtility.FromJson<DoubaoServerResponse>(payloadJson);
        }
        catch (Exception ex)
        {
            if (enableUploadDebugLog)
            {
                UnityEngine.Debug.LogWarning($"[豆包上传调试] JSON解析失败，片段前120: {SafeHead(payloadJson, 120)} | {ex.Message}");
            }

            if (isLastServerPacket)
            {
                string finalTextOnParseFail = !string.IsNullOrWhiteSpace(_lastDefiniteText) ? _lastDefiniteText : _lastText;
                RunOnMainThread(() => CompleteRecognitionOnMainThread(finalTextOnParseFail));
            }

            return;
        }

        if (response?.result == null)
        {
            if (isLastServerPacket)
            {
                string finalTextOnNullResult = !string.IsNullOrWhiteSpace(_lastDefiniteText) ? _lastDefiniteText : _lastText;
                RunOnMainThread(() => CompleteRecognitionOnMainThread(finalTextOnNullResult));
            }
            return;
        }

        string fullText = response.result.text ?? "";
        if (!string.IsNullOrWhiteSpace(fullText) && fullText != _lastText)
        {
            _lastText = fullText;

            if (enableUploadDebugLog && !_loggedFirstServerText)
            {
                _loggedFirstServerText = true;
                UnityEngine.Debug.Log("[豆包上传调试] 已收到首条识别文本回传");
            }

            RunOnMainThread(() => OnPartialResult?.Invoke(fullText));
        }

        if (response.result.utterances != null && response.result.utterances.Count > 0)
        {
            for (int i = 0; i < response.result.utterances.Count; i++)
            {
                var utterance = response.result.utterances[i];
                if (utterance != null && utterance.definite && !string.IsNullOrWhiteSpace(utterance.text) && utterance.text != _lastDefiniteText)
                {
                    _lastDefiniteText = utterance.text;
                    string finalPiece = utterance.text;
                    RunOnMainThread(() => OnFinalResult?.Invoke(finalPiece));
                }
            }
        }

        if (isLastServerPacket)
        {
            string finalText = !string.IsNullOrWhiteSpace(_lastDefiniteText) ? _lastDefiniteText : _lastText;
            RunOnMainThread(() => CompleteRecognitionOnMainThread(finalText));
        }
    }

    private void HandleServerTextJson(string rawJson, bool isLastServerPacket)
    {
        if (!TryExtractFirstJsonObject(rawJson, out var payloadJson))
        {
            if (isLastServerPacket)
            {
                string finalText = !string.IsNullOrWhiteSpace(_lastDefiniteText) ? _lastDefiniteText : _lastText;
                RunOnMainThread(() => CompleteRecognitionOnMainThread(finalText));
            }
            return;
        }

        DoubaoServerResponse response;
        try
        {
            response = JsonUtility.FromJson<DoubaoServerResponse>(payloadJson);
        }
        catch (Exception ex)
        {
            if (enableUploadDebugLog)
            {
                UnityEngine.Debug.LogWarning($"[豆包上传调试] 文本帧JSON解析失败: {SafeHead(payloadJson, 120)} | {ex.Message}");
            }

            if (isLastServerPacket)
            {
                string finalText = !string.IsNullOrWhiteSpace(_lastDefiniteText) ? _lastDefiniteText : _lastText;
                RunOnMainThread(() => CompleteRecognitionOnMainThread(finalText));
            }
            return;
        }

        if (response?.result == null)
        {
            if (isLastServerPacket)
            {
                string finalText = !string.IsNullOrWhiteSpace(_lastDefiniteText) ? _lastDefiniteText : _lastText;
                RunOnMainThread(() => CompleteRecognitionOnMainThread(finalText));
            }
            return;
        }

        string fullText = response.result.text ?? "";
        if (!string.IsNullOrWhiteSpace(fullText) && fullText != _lastText)
        {
            _lastText = fullText;

            if (enableUploadDebugLog && !_loggedFirstServerText)
            {
                _loggedFirstServerText = true;
                UnityEngine.Debug.Log("[豆包上传调试] 已收到首条识别文本回传(文本帧)");
            }

            RunOnMainThread(() => OnPartialResult?.Invoke(fullText));
        }

        if (response.result.utterances != null && response.result.utterances.Count > 0)
        {
            for (int i = 0; i < response.result.utterances.Count; i++)
            {
                var utterance = response.result.utterances[i];
                if (utterance != null && utterance.definite && !string.IsNullOrWhiteSpace(utterance.text) && utterance.text != _lastDefiniteText)
                {
                    _lastDefiniteText = utterance.text;
                    string finalPiece = utterance.text;
                    RunOnMainThread(() => OnFinalResult?.Invoke(finalPiece));
                }
            }
        }

        if (isLastServerPacket)
        {
            string finalText = !string.IsNullOrWhiteSpace(_lastDefiniteText) ? _lastDefiniteText : _lastText;
            RunOnMainThread(() => CompleteRecognitionOnMainThread(finalText));
        }
    }

    private void CompleteRecognitionOnMainThread(string finalText)
    {
        if (_recognitionCompletedRaised)
        {
            return;
        }

        CancelWaitFinalFallback();

        _recognitionCompletedRaised = true;
        OnRecognitionCompleted?.Invoke(finalText ?? string.Empty);
        Disconnect();
    }

    private void StartWaitFinalFallback()
    {
        CancelWaitFinalFallback();

        if (finalResultFallbackTimeoutSeconds <= 0f)
        {
            return;
        }

        _waitFinalFallbackCoroutine = StartCoroutine(WaitFinalFallbackCoroutine());
    }

    private void CancelWaitFinalFallback()
    {
        if (_waitFinalFallbackCoroutine != null)
        {
            StopCoroutine(_waitFinalFallbackCoroutine);
            _waitFinalFallbackCoroutine = null;
        }
    }

    private IEnumerator WaitFinalFallbackCoroutine()
    {
        yield return new WaitForSeconds(finalResultFallbackTimeoutSeconds);

        if (_recognitionCompletedRaised)
        {
            yield break;
        }

        string finalText = !string.IsNullOrWhiteSpace(_lastDefiniteText) ? _lastDefiniteText : _lastText;

        if (enableUploadDebugLog)
        {
            UnityEngine.Debug.Log("[豆包上传调试] 超时兜底触发完成回调");
        }

        CompleteRecognitionOnMainThread(finalText);
    }

    public void Disconnect()
    {
        CancelWaitFinalFallback();

        try
        {
            if (_ws != null)
            {
                if (_ws.ReadyState == WebSocketState.Open || _ws.ReadyState == WebSocketState.Connecting)
                {
                    _ws.CloseAsync();
                }

                _ws = null;
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("关闭 WebSocket 时异常: " + e.Message);
        }

        _sessionStarted = false;
    }

    private void RunOnMainThread(Action action)
    {
        if (action == null)
        {
            return;
        }

        if (System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId)
        {
            action.Invoke();
            return;
        }

        lock (_mainThreadActionLock)
        {
            _mainThreadActions.Enqueue(action);
        }
    }

    private void FlushMainThreadActions()
    {
        while (true)
        {
            Action action = null;

            lock (_mainThreadActionLock)
            {
                if (_mainThreadActions.Count > 0)
                {
                    action = _mainThreadActions.Dequeue();
                }
            }

            if (action == null)
            {
                break;
            }

            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[DoubaoSpeechToText] 主线程回调执行失败: " + ex);
            }
        }
    }

    private byte[] ShortsToBytes(short[] samples)
    {
        byte[] bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            byte[] temp = BitConverter.GetBytes(samples[i]);
            bytes[i * 2] = temp[0];
            bytes[i * 2 + 1] = temp[1];
        }

        return bytes;
    }

    private string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private void ReportStatus(string message)
    {
        RunOnMainThread(() => { OnStatusUpdated?.Invoke(message); });
    }

    private void ReportError(string error)
    {
        RunOnMainThread(() =>
        {
            UnityEngine.Debug.LogError("[DoubaoSpeechToText] " + error);
            OnError?.Invoke(error);
        });
    }

    private static double GetNowSec()
    {
        return _clock.Elapsed.TotalSeconds;
    }

    private static bool TryExtractFirstJsonObject(string raw, out string json)
    {
        json = null;
        if (string.IsNullOrEmpty(raw)) return false;
        string s = raw.Trim('\0', ' ', '\t', '\r', '\n', '\uFEFF');
        if (string.IsNullOrEmpty(s)) return false;

        int start = -1;
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"')
            {
                inString = true;
                continue;
            }
            if (c == '{')
            {
                if (depth == 0) start = i;
                depth++;
                continue;
            }
            if (c == '}')
            {
                if (depth <= 0) continue;
                depth--;
                if (depth == 0 && start >= 0)
                {
                    json = s.Substring(start, i - start + 1);
                    return true;
                }
            }
        }
        return false;
    }

    private static string SafeHead(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxLen ? s : s.Substring(0, maxLen);
    }
}