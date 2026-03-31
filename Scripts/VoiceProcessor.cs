using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VoiceProcessor : MonoBehaviour
{
    public bool IsRecording
    {
        get { return _audioClip != null && Microphone.IsRecording(CurrentDeviceName); }
    }

    public bool HasDetectedSpeech
    {
        get { return _hasDetectedSpeech; }
    }

    [SerializeField] private int MicrophoneIndex;

    public int SampleRate { get; private set; }
    public int FrameLength { get; private set; }

    public event Action<short[]> OnFrameCaptured;
    public event Action OnRecordingStop;
    public event Action OnRecordingStart;
    public event Action OnNoSpeechTimeout;
    public event Action OnSilenceTimeout;

    public List<string> Devices { get; private set; }
    public int CurrentDeviceIndex { get; private set; }

    public string CurrentDeviceName
    {
        get
        {
            if (CurrentDeviceIndex < 0 || CurrentDeviceIndex >= Microphone.devices.Length)
            {
                return string.Empty;
            }

            return Devices[CurrentDeviceIndex];
        }
    }

    [Header("Voice Detection Settings")]
    [SerializeField, Range(0.0f, 1.0f)]
    private float _miniSampleValue = 0.05f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float _miniRmsValue = 0.015f;

    [SerializeField]
    private float _silenceTimer = 1.0f;

    [SerializeField]
    private float _noSpeechTimeout = 2.0f;

    [SerializeField]
    private bool _autoDetect = true;

    [SerializeField]
    private bool _enableNoSpeechTimeoutStop = true;

    private float _timeAtSilenceBegan;
    private float _recordingStartTime;
    private bool _audioDetected;
    private bool _transmit;
    private bool _hasDetectedSpeech;
    private bool _stopEventRaised;

    // 避免超时事件每帧重复触发
    private bool _silenceTimeoutRaised;
    private bool _noSpeechTimeoutRaised;

    private AudioClip _audioClip;
    private Coroutine _recordCoroutine;
    private event Action RestartRecording;

    private void Awake()
    {
        UpdateDevices();
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (CurrentDeviceIndex != MicrophoneIndex)
        {
            ChangeDevice(MicrophoneIndex);
        }
    }
#endif

    public void UpdateDevices()
    {
        Devices = new List<string>();
        foreach (var device in Microphone.devices)
        {
            Devices.Add(device);
        }

        if (Devices == null || Devices.Count == 0)
        {
            CurrentDeviceIndex = -1;
            Debug.LogError("There is no valid recording device connected");
            return;
        }

        CurrentDeviceIndex = Mathf.Clamp(MicrophoneIndex, 0, Devices.Count - 1);
    }

    public void ChangeDevice(int deviceIndex)
    {
        if (Devices == null || deviceIndex < 0 || deviceIndex >= Devices.Count)
        {
            Debug.LogError($"Specified device index {deviceIndex} is not a valid recording device");
            return;
        }

        if (IsRecording)
        {
            RestartRecording += () =>
            {
                CurrentDeviceIndex = deviceIndex;
                StartRecording(SampleRate, FrameLength, _autoDetect);
            };

            StopRecording();
        }
        else
        {
            CurrentDeviceIndex = deviceIndex;
        }
    }

    // 独立的热切换方法，不中断物理麦克风
    public void SetAutoDetect(bool autoDetect)
    {
        _autoDetect = autoDetect;
        if (_autoDetect)
        {
            _recordingStartTime = Time.time;
            _timeAtSilenceBegan = Time.time;
            _audioDetected = false;
            _transmit = false;
            _hasDetectedSpeech = false;
            _silenceTimeoutRaised = false;
            _noSpeechTimeoutRaised = false;
        }
        else
        {
            _transmit = true;
            _audioDetected = true;
            _recordingStartTime = Time.time;
            _timeAtSilenceBegan = Time.time;
            _silenceTimeoutRaised = false;
            _noSpeechTimeoutRaised = false;
        }
    }

    public void StartRecording(int sampleRate = 16000, int frameSize = 512, bool? autoDetect = null)
    {
        bool requestedAutoDetect = autoDetect ?? _autoDetect;

        if (IsRecording)
        {
            // 只要采样率或帧率没变，仅仅是切换 VAD 状态的话，【绝对不要】停止物理麦克风
            bool needRestart = sampleRate != SampleRate || frameSize != FrameLength;

            if (needRestart)
            {
                RestartRecording += () =>
                {
                    StartRecording(sampleRate, frameSize, requestedAutoDetect);
                };
                StopRecording();
            }
            else
            {
                // 无缝热切换 VAD（实现预热和正式录音切换且声音不打断）
                SetAutoDetect(requestedAutoDetect);
            }
            return;
        }

        SampleRate = sampleRate;
        FrameLength = frameSize;
        _stopEventRaised = false;
        SetAutoDetect(requestedAutoDetect);

        if (string.IsNullOrEmpty(CurrentDeviceName))
        {
            Debug.LogError("当前没有可用麦克风设备");
            return;
        }

        _audioClip = Microphone.Start(CurrentDeviceName, true, 1, sampleRate);
        _recordCoroutine = StartCoroutine(RecordData());
    }

    public void StopRecording()
    {
        StopRecordingInternal();
    }

    private void StopRecordingInternal()
    {
        if (_audioClip == null && !IsRecording)
        {
            return;
        }

        if (Microphone.IsRecording(CurrentDeviceName))
        {
            Microphone.End(CurrentDeviceName);
        }

        if (_recordCoroutine != null)
        {
            StopCoroutine(_recordCoroutine);
            _recordCoroutine = null;
        }

        if (_audioClip != null)
        {
            Destroy(_audioClip);
            _audioClip = null;
        }

        RaiseRecordingStopOnce();

        var restart = RestartRecording;
        RestartRecording = null;
        restart?.Invoke();
    }

    private void RaiseRecordingStopOnce()
    {
        if (_stopEventRaised)
        {
            return;
        }

        _stopEventRaised = true;
        OnRecordingStop?.Invoke();
    }

    private IEnumerator RecordData()
    {
        float[] sampleBuffer = new float[FrameLength];
        int startReadPos = 0;

        OnRecordingStart?.Invoke();

        while (IsRecording)
        {
            int curClipPos = Microphone.GetPosition(CurrentDeviceName);
            if (curClipPos < startReadPos)
            {
                curClipPos += _audioClip.samples;
            }

            int samplesAvailable = curClipPos - startReadPos;
            if (samplesAvailable < FrameLength)
            {
                yield return null;
                continue;
            }

            int endReadPos = startReadPos + FrameLength;
            if (endReadPos > _audioClip.samples)
            {
                int numSamplesClipEnd = _audioClip.samples - startReadPos;
                float[] endClipSamples = new float[numSamplesClipEnd];
                _audioClip.GetData(endClipSamples, startReadPos);

                int numSamplesClipStart = endReadPos - _audioClip.samples;
                float[] startClipSamples = new float[numSamplesClipStart];
                _audioClip.GetData(startClipSamples, 0);

                Array.Copy(endClipSamples, 0, sampleBuffer, 0, numSamplesClipEnd);
                Array.Copy(startClipSamples, 0, sampleBuffer, numSamplesClipEnd, numSamplesClipStart);
            }
            else
            {
                _audioClip.GetData(sampleBuffer, startReadPos);
            }

            startReadPos = endReadPos % _audioClip.samples;

            if (_autoDetect == false)
            {
                // 预热模式：源源不断接收音频但是不做静音裁断
                _transmit = true;
                _audioDetected = true;
                _timeAtSilenceBegan = Time.time;
                _silenceTimeoutRaised = false;
                _noSpeechTimeoutRaised = false;
            }
            else
            {
                float maxVolume = 0.0f;
                float sumSquares = 0.0f;

                for (int i = 0; i < sampleBuffer.Length; i++)
                {
                    float v = sampleBuffer[i];
                    float abs = Mathf.Abs(v);
                    if (abs > maxVolume) maxVolume = abs;
                    sumSquares += v * v;
                }

                float rms = Mathf.Sqrt(sumSquares / sampleBuffer.Length);
                bool isSpeechFrame = maxVolume >= _miniSampleValue && rms >= _miniRmsValue;

                if (isSpeechFrame)
                {
                    _transmit = true;
                    _audioDetected = true;
                    _hasDetectedSpeech = true;
                    _timeAtSilenceBegan = Time.time;
                    _silenceTimeoutRaised = false;
                    _noSpeechTimeoutRaised = false;
                }
                else
                {
                    _transmit = false;

                    if (!_hasDetectedSpeech)
                    {
                        if (_enableNoSpeechTimeoutStop &&
                            !_noSpeechTimeoutRaised &&
                            Time.time - _recordingStartTime >= _noSpeechTimeout)
                        {
                            _noSpeechTimeoutRaised = true;
                            OnNoSpeechTimeout?.Invoke(); // 不关录音，单纯外调事件
                        }
                    }
                    else
                    {
                        if (!_silenceTimeoutRaised && Time.time - _timeAtSilenceBegan >= _silenceTimer)
                        {
                            _silenceTimeoutRaised = true;
                            _hasDetectedSpeech = false;
                            _audioDetected = false;
                            OnSilenceTimeout?.Invoke(); // 不关录音，单纯外调事件
                        }
                    }
                }
            }

            if (_audioDetected && _transmit)
            {
                short[] pcmBuffer = new short[sampleBuffer.Length];
                for (int i = 0; i < FrameLength; i++)
                {
                    pcmBuffer[i] = (short)Mathf.Clamp(
                        Mathf.RoundToInt(sampleBuffer[i] * short.MaxValue),
                        short.MinValue,
                        short.MaxValue);
                }

                OnFrameCaptured?.Invoke(pcmBuffer);
            }
        }

        RaiseRecordingStopOnce();
        var restart = RestartRecording;
        RestartRecording = null;
        restart?.Invoke();
    }
}