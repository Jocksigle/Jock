using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
//using Vosk;

/// <summary>
/// 1) 从 json(TextAsset) 读取题库
/// 2) 基于 N-Gram + 同义词进行匹配
/// 3) 输出匹配结果（问题id + 音频名字）
/// 4) 加载并播放本地音频
/// </summary>
public class AudioRetrieval_JS : MonoBehaviour
{
    //public VoskDialogText vosk;

    [Serializable]
    public class SynonymGroup
    {
        public string name;
        public List<string> words = new List<string>();
    }

    [Serializable]
    private class JsonRoot
    {
        public List<JsonItem> items = new List<JsonItem>();
    }

    [Serializable]
    private class JsonItem
    {
        public int id;
        public string question;
        public string audios;
    }

    private class RuntimeEntry
    {
        public int id;
        public string question;
        public string audioName;
        public List<string> audioNames = new List<string>();
        public string normalizedQuestion;
        public HashSet<string> biGrams = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> triGrams = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> filteredBiGrams = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> filteredTriGrams = new HashSet<string>(StringComparer.Ordinal);
    }

    [Serializable]
    public class MatchResult
    {
        public bool isMatched;
        public int questionId;
        public string questionText;
        public string audioName;
        public float score;
    }

    [Header("Data Source(JSON)")]
    [SerializeField] private TextAsset questionJson;

    [Header("Audio Name Build")]
    [SerializeField] private string audioNamePrefix = "";
    [SerializeField] private string audioNameSuffix = ".mp3";
    [SerializeField] private string fallbackAudioName = "fallback.mp3";

    [Header("Similarity")]
    [Range(0f, 1f)]
    [SerializeField] private float minMatchScore = 0.22f;
    [Range(0f, 1f)]
    [SerializeField] private float nGramWeight = 0.8f;
    [Range(0f, 1f)]
    [SerializeField] private float synonymWeight = 0.2f;
    [SerializeField] private List<SynonymGroup> synonymGroups = new List<SynonymGroup>();

    [Header("Similarity Advanced")]
    [Range(0f, 1f)]
    [SerializeField] private float biGramBlend = 0.65f;
    [Range(0f, 1f)]
    [SerializeField] private float commonGramDocumentRate = 0.35f;
    [SerializeField] private int minCommonGramDocumentCount = 3;
    [Range(0f, 0.3f)]
    [SerializeField] private float containBonus = 0.12f;
    [Range(0f, 0.3f)]
    [SerializeField] private float lengthPenaltyWeight = 0.08f;

    [Header("Audio Playback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private string localAudioRootPath = "LocalAudios/";
    [SerializeField] private bool usePersistentDataPath = false;
    [SerializeField] private float audioVolume = 1f;

    [Header("Pre-Matching")]
    [SerializeField] private bool enablePreMatch = true;
    [Range(0f, 1f)]
    [SerializeField] private float preMatchMinScore = 0.18f;

    public event Action<MatchResult> OnMatchCompleted;
    public event Action OnAudioPlayCompleted;

    private readonly List<RuntimeEntry> runtimeEntries = new List<RuntimeEntry>();
    private readonly Dictionary<string, int> synonymWordToGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> biGramDocumentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> triGramDocumentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<int, string> lastPlayedAudioNameByQuestionId = new Dictionary<int, string>();
    private readonly Dictionary<string, AudioClip> preloadedAudioClips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

    private string preMatchedAudioName;
    private int preMatchedQuestionId = -1;
    private float preMatchedScore = 0f;

    private AudioClip currentAudioClip;
    private int documentCount;
    private int _playRequestVersion;

    public bool IsPlayingAudio => audioSource != null && audioSource.isPlaying;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.volume = audioVolume;
        audioSource.playOnAwake = false;

        BuildSynonymIndex();
        LoadFromJson();
    }

    private void OnDestroy()
    {
        CancelInvoke(nameof(OnAudioPlayFinished));

        if (currentAudioClip != null && !IsClipFromPreloadedCache(currentAudioClip))
        {
            Destroy(currentAudioClip);
            currentAudioClip = null;
        }

        foreach (var kv in preloadedAudioClips)
        {
            if (kv.Value != null)
            {
                Destroy(kv.Value);
            }
        }

        preloadedAudioClips.Clear();
    }

    private void LoadFromJson()
    {
        runtimeEntries.Clear();
        biGramDocumentFrequency.Clear();
        triGramDocumentFrequency.Clear();
        lastPlayedAudioNameByQuestionId.Clear();
        documentCount = 0;

        if (questionJson == null || string.IsNullOrWhiteSpace(questionJson.text))
        {
            Debug.LogError("题库JSON文件为空或未赋值！");
            return;
        }

        string jsonText = WrapJsonArray(questionJson.text.Trim());

        try
        {
            var root = JsonUtility.FromJson<JsonRoot>(jsonText);
            if (root == null || root.items == null)
            {
                Debug.LogError("JSON解析后根节点或items数组为空！");
                return;
            }

            foreach (var item in root.items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.question))
                {
                    Debug.LogWarning($"跳过无效题库项（id:{item?.id}），问题文本为空");
                    continue;
                }

                var audioNames = BuildAudioNames(item.audios);
                if (audioNames.Count == 0)
                {
                    Debug.LogWarning($"题库项未解析到音频（id:{item.id} question:{item.question} audios:{item.audios})");
                }

                var normalizedQuestion = NormalizeText(item.question);
                var biGrams = BuildNGrams(normalizedQuestion, 2);
                var triGrams = BuildNGrams(normalizedQuestion, 3);

                var entry = new RuntimeEntry
                {
                    id = item.id,
                    question = item.question,
                    audioName = audioNames.Count > 0 ? audioNames[0] : fallbackAudioName,
                    audioNames = audioNames,
                    normalizedQuestion = normalizedQuestion,
                    biGrams = biGrams,
                    triGrams = triGrams
                };

                runtimeEntries.Add(entry);
                UpdateDocumentFrequency(biGrams, biGramDocumentFrequency);
                UpdateDocumentFrequency(triGrams, triGramDocumentFrequency);
            }

            documentCount = runtimeEntries.Count;

            foreach (var entry in runtimeEntries)
            {
                entry.filteredBiGrams = FilterCommonGrams(entry.biGrams, biGramDocumentFrequency);
                entry.filteredTriGrams = FilterCommonGrams(entry.triGrams, triGramDocumentFrequency);
            }

            Debug.Log($"成功加载{runtimeEntries.Count}条题库数据");
        }
        catch (Exception e)
        {
            Debug.LogError($"JSON解析失败！错误信息：{e.Message}\nJSON内容：{jsonText}");
        }
    }

    private string WrapJsonArray(string jsonText)
    {
        if (jsonText.StartsWith("[") && jsonText.EndsWith("]"))
        {
            return $"{{\"items\":{jsonText}}}";
        }

        return jsonText;
    }

    public MatchResult MatchByInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || runtimeEntries.Count == 0)
        {
            Debug.LogError("输入文本为空或题库未加载，播放兜底音频");
            var fallbackResult = BuildFallbackResult();
            PlayLocalAudio(fallbackResult.audioName);
            OnMatchCompleted?.Invoke(fallbackResult);
            return fallbackResult;
        }

        RuntimeEntry bestEntry = null;
        var bestScore = float.MinValue;

        foreach (var entry in runtimeEntries)
        {
            var score = ComputeSimilarity(input, entry);
            if (score > bestScore)
            {
                bestScore = score;
                bestEntry = entry;
            }
        }

        if (bestEntry == null || bestScore < minMatchScore || bestEntry.audioNames == null || bestEntry.audioNames.Count == 0)
        {
            var fallbackResult = BuildFallbackResult();
            PlayLocalAudio(fallbackResult.audioName);
            OnMatchCompleted?.Invoke(fallbackResult);
            return fallbackResult;
        }

        var selectedAudioName = GetRandomAudioName(bestEntry.id, bestEntry.audioNames);

        if (bestEntry.id == preMatchedQuestionId && !string.IsNullOrWhiteSpace(preMatchedAudioName))
        {
            selectedAudioName = preMatchedAudioName;
        }

        Debug.Log($"匹配成功：{bestEntry.question} | 音频名：{selectedAudioName} | 匹配度：{bestScore:F4}");
        AddSystemMessage($"匹配成功：{bestEntry.question} | 音频名：{selectedAudioName} | 匹配度：{bestScore:F4}");

        var matchResult = new MatchResult
        {
            isMatched = true,
            questionId = bestEntry.id,
            questionText = bestEntry.question,
            audioName = selectedAudioName,
            score = bestScore
        };

        PlayLocalAudio(selectedAudioName);
        OnMatchCompleted?.Invoke(matchResult);
        return matchResult;
    }

    public void OnServerTextReceived(string serverText)
    {
        Debug.Log($"收到服务端文本：{serverText}");
        MatchByInput(serverText);
    }

    public void PreMatchByInput(string input)
    {
        if (!enablePreMatch || string.IsNullOrWhiteSpace(input) || runtimeEntries.Count == 0)
        {
            return;
        }

        RuntimeEntry bestEntry = null;
        var bestScore = float.MinValue;

        foreach (var entry in runtimeEntries)
        {
            var score = ComputeSimilarity(input, entry);
            if (score > bestScore)
            {
                bestScore = score;
                bestEntry = entry;
            }
        }

        if (bestEntry == null || bestScore < preMatchMinScore || bestEntry.audioNames == null || bestEntry.audioNames.Count == 0)
        {
            return;
        }

        var selectedAudioName = GetRandomAudioName(bestEntry.id, bestEntry.audioNames);

        bool changed = bestEntry.id != preMatchedQuestionId ||
                       !string.Equals(selectedAudioName, preMatchedAudioName, StringComparison.OrdinalIgnoreCase);

        preMatchedQuestionId = bestEntry.id;
        preMatchedAudioName = selectedAudioName;
        preMatchedScore = bestScore;

        if (changed)
        {
            _ = PreloadAudioClipAsync(selectedAudioName);
            Debug.Log($"预匹配成功：{bestEntry.question} | 音频名：{selectedAudioName} | 匹配度：{bestScore:F4}");
        }
    }

    public void ClearPreMatch()
    {
        preMatchedQuestionId = -1;
        preMatchedAudioName = null;
        preMatchedScore = 0f;
    }

    private async void PlayLocalAudio(string audioFileName)
    {
        int requestVersion = ++_playRequestVersion;
        CancelInvoke(nameof(OnAudioPlayFinished));

        if (audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        if (currentAudioClip != null)
        {
            if (!IsClipFromPreloadedCache(currentAudioClip))
            {
                Destroy(currentAudioClip);
            }
            currentAudioClip = null;
        }

        AudioClip loadedClip = null;

        if (!string.IsNullOrWhiteSpace(audioFileName)
            && preloadedAudioClips.TryGetValue(audioFileName, out var preloadedClip)
            && preloadedClip != null)
        {
            loadedClip = preloadedClip;
        }
        else
        {
            loadedClip = await LoadAudioClipFromLocal(audioFileName, false);
        }

        if (requestVersion != _playRequestVersion)
        {
            if (loadedClip != null && !IsClipFromPreloadedCache(loadedClip))
            {
                Destroy(loadedClip);
            }
            return;
        }

        if (loadedClip == null)
        {
            Debug.LogWarning($"目标音频 {audioFileName} 加载失败，尝试兜底音频 {fallbackAudioName}");
            loadedClip = await LoadAudioClipFromLocal(fallbackAudioName, true);

            if (requestVersion != _playRequestVersion)
            {
                if (loadedClip != null && !IsClipFromPreloadedCache(loadedClip))
                {
                    Destroy(loadedClip);
                }
                return;
            }
        }

        if (loadedClip == null)
        {
            Debug.LogError("所有音频加载失败，无法播放");
            OnAudioPlayCompleted?.Invoke();
            return;
        }

        currentAudioClip = loadedClip;
        audioSource.clip = currentAudioClip;
        audioSource.Play();

        Debug.Log($"开始播放音频：{audioFileName}");
        AddSystemMessage($"开始播放音频：{audioFileName}");
        Invoke(nameof(OnAudioPlayFinished), currentAudioClip.length);
    }

    private bool IsClipFromPreloadedCache(AudioClip clip)
    {
        if (clip == null)
        {
            return false;
        }

        foreach (var kv in preloadedAudioClips)
        {
            if (kv.Value == clip)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<AudioClip> LoadAudioClipFromLocal(string fileName, bool isFallback = false)
    {
        string basePath = usePersistentDataPath ? Application.persistentDataPath : Application.streamingAssetsPath;
        string fullPath = Path.Combine(basePath, localAudioRootPath, fileName);

        string url;
        if (usePersistentDataPath)
        {
            url = $"file://{fullPath}";
        }
        else
        {
            url = fullPath;
        }

        try
        {
            var audioType = GetAudioTypeFromPath(fileName);

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                var operation = www.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"加载音频失败：{www.error} | 路径：{url}");
                    return null;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip == null || clip.loadState != AudioDataLoadState.Loaded)
                {
                    Debug.LogError($"音频解码失败：{url}");
                    return null;
                }

                return clip;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"加载音频异常：{e.Message} | 路径：{url}");
            return null;
        }
    }

    private async Task PreloadAudioClipAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        if (preloadedAudioClips.ContainsKey(fileName) && preloadedAudioClips[fileName] != null)
        {
            return;
        }

        var clip = await LoadAudioClipFromLocal(fileName, false);
        if (clip != null)
        {
            preloadedAudioClips[fileName] = clip;
        }
    }

    private void OnAudioPlayFinished()
    {
        Debug.Log("音频播放完成");
        AddSystemMessage("音频播放完成");
        OnAudioPlayCompleted?.Invoke();
    }

    public void OnwangPlayFinished()
    {
        Debug.Log("网络不正常");
        AddSystemMessage("网络不正常");
    }

    public void OnAudioNoPlay()
    {
    }

    private MatchResult BuildFallbackResult()
    {
        return new MatchResult
        {
            isMatched = false,
            questionId = -1,
            questionText = string.Empty,
            audioName = fallbackAudioName,
            score = 0f
        };
    }

    private string BuildAudioName(int audioId)
    {
        return $"{audioNamePrefix}{audioId}{audioNameSuffix}";
    }

    private List<string> BuildAudioNames(string audiosRaw)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(audiosRaw))
        {
            return results;
        }

        var parts = audiosRaw.Split(new[] { '|', ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            results.Add($"{audioNamePrefix}{trimmed}{audioNameSuffix}");
        }

        return results;
    }

    private string GetRandomAudioName(List<string> audioNames)
    {
        if (audioNames == null || audioNames.Count == 0)
        {
            return fallbackAudioName;
        }

        var index = UnityEngine.Random.Range(0, audioNames.Count);
        return audioNames[index];
    }

    private string GetRandomAudioName(int questionId, List<string> audioNames)
    {
        if (audioNames == null || audioNames.Count == 0)
        {
            return fallbackAudioName;
        }

        if (audioNames.Count == 1)
        {
            lastPlayedAudioNameByQuestionId[questionId] = audioNames[0];
            return audioNames[0];
        }

        if (!lastPlayedAudioNameByQuestionId.TryGetValue(questionId, out var lastAudioName) || string.IsNullOrWhiteSpace(lastAudioName))
        {
            var firstIndex = UnityEngine.Random.Range(0, audioNames.Count);
            var firstSelected = audioNames[firstIndex];
            lastPlayedAudioNameByQuestionId[questionId] = firstSelected;
            return firstSelected;
        }

        var candidates = new List<string>();
        foreach (var audioName in audioNames)
        {
            if (!string.Equals(audioName, lastAudioName, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(audioName);
            }
        }

        if (candidates.Count == 0)
        {
            lastPlayedAudioNameByQuestionId[questionId] = audioNames[0];
            return audioNames[0];
        }

        var index = UnityEngine.Random.Range(0, candidates.Count);
        var selected = candidates[index];
        lastPlayedAudioNameByQuestionId[questionId] = selected;
        return selected;
    }

    private void BuildSynonymIndex()
    {
        synonymWordToGroup.Clear();

        for (var i = 0; i < synonymGroups.Count; i++)
        {
            var group = synonymGroups[i];
            if (group == null || group.words == null)
            {
                continue;
            }

            foreach (var word in group.words)
            {
                if (string.IsNullOrWhiteSpace(word))
                {
                    continue;
                }

                var key = NormalizeText(word);
                if (!string.IsNullOrEmpty(key))
                {
                    synonymWordToGroup[key] = i;
                }
            }
        }
    }

    private float ComputeSimilarity(string input, string document)
    {
        var normalizedDocument = NormalizeText(document);

        var tempEntry = new RuntimeEntry
        {
            question = document,
            normalizedQuestion = normalizedDocument,
            biGrams = BuildNGrams(normalizedDocument, 2),
            triGrams = BuildNGrams(normalizedDocument, 3)
        };

        tempEntry.filteredBiGrams = FilterCommonGrams(tempEntry.biGrams, biGramDocumentFrequency);
        tempEntry.filteredTriGrams = FilterCommonGrams(tempEntry.triGrams, triGramDocumentFrequency);

        return ComputeSimilarity(input, tempEntry);
    }

    private float ComputeSimilarity(string input, RuntimeEntry entry)
    {
        var inputNorm = NormalizeText(input);
        if (string.IsNullOrEmpty(inputNorm) || string.IsNullOrEmpty(entry.normalizedQuestion))
        {
            return 0f;
        }

        var rawInputBiGrams = BuildNGrams(inputNorm, 2);
        var rawInputTriGrams = BuildNGrams(inputNorm, 3);

        var filteredInputBiGrams = FilterCommonGrams(rawInputBiGrams, biGramDocumentFrequency);
        var filteredInputTriGrams = FilterCommonGrams(rawInputTriGrams, triGramDocumentFrequency);

        var effectiveInputBiGrams = filteredInputBiGrams.Count > 0 ? filteredInputBiGrams : rawInputBiGrams;
        var effectiveInputTriGrams = filteredInputTriGrams.Count > 0 ? filteredInputTriGrams : rawInputTriGrams;

        var effectiveEntryBiGrams = entry.filteredBiGrams.Count > 0 ? entry.filteredBiGrams : entry.biGrams;
        var effectiveEntryTriGrams = entry.filteredTriGrams.Count > 0 ? entry.filteredTriGrams : entry.triGrams;

        var biScore = ComputeWeightedDiceSimilarity(effectiveInputBiGrams, effectiveEntryBiGrams, biGramDocumentFrequency);
        var triScore = ComputeWeightedDiceSimilarity(effectiveInputTriGrams, effectiveEntryTriGrams, triGramDocumentFrequency);
        var nGramScore = biScore * biGramBlend + triScore * (1f - biGramBlend);

        var synonymScore = ComputeSynonymSimilarity(inputNorm, entry.normalizedQuestion);

        var totalWeight = Mathf.Max(0.0001f, nGramWeight + synonymWeight);
        var score = (nGramScore * nGramWeight + synonymScore * synonymWeight) / totalWeight;

        if (entry.normalizedQuestion.Contains(inputNorm) || inputNorm.Contains(entry.normalizedQuestion))
        {
            score = Mathf.Min(1f, score + containBonus);
        }

        score -= ComputeLengthPenalty(inputNorm.Length, entry.normalizedQuestion.Length);
        return Mathf.Clamp01(score);
    }

    private static void UpdateDocumentFrequency(HashSet<string> grams, Dictionary<string, int> documentFrequency)
    {
        foreach (var gram in grams)
        {
            if (documentFrequency.TryGetValue(gram, out var count))
            {
                documentFrequency[gram] = count + 1;
            }
            else
            {
                documentFrequency[gram] = 1;
            }
        }
    }

    private HashSet<string> FilterCommonGrams(HashSet<string> rawGrams, Dictionary<string, int> documentFrequency)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var gram in rawGrams)
        {
            if (!IsCommonGram(gram, documentFrequency))
            {
                result.Add(gram);
            }
        }

        return result;
    }

    private bool IsCommonGram(string gram, Dictionary<string, int> documentFrequency)
    {
        if (documentCount <= 0)
        {
            return false;
        }

        if (!documentFrequency.TryGetValue(gram, out var count))
        {
            return false;
        }

        if (count < minCommonGramDocumentCount)
        {
            return false;
        }

        return (float)count / documentCount >= commonGramDocumentRate;
    }

    private static float ComputeNGramSimilarity(string a, string b, int n)
    {
        var gramsA = BuildNGrams(a, n);
        var gramsB = BuildNGrams(b, n);

        if (gramsA.Count == 0 || gramsB.Count == 0)
        {
            return 0f;
        }

        var intersect = 0;
        foreach (var g in gramsA)
        {
            if (gramsB.Contains(g))
            {
                intersect++;
            }
        }

        return (2f * intersect) / (gramsA.Count + gramsB.Count);
    }

    private float ComputeWeightedDiceSimilarity(
        HashSet<string> inputGrams,
        HashSet<string> documentGrams,
        Dictionary<string, int> documentFrequency)
    {
        if (inputGrams.Count == 0 || documentGrams.Count == 0)
        {
            return 0f;
        }

        var inputWeightSum = 0f;
        var documentWeightSum = 0f;
        var intersectionWeightSum = 0f;

        foreach (var gram in inputGrams)
        {
            inputWeightSum += GetGramWeight(gram, documentFrequency);
        }

        foreach (var gram in documentGrams)
        {
            documentWeightSum += GetGramWeight(gram, documentFrequency);
        }

        foreach (var gram in inputGrams)
        {
            if (documentGrams.Contains(gram))
            {
                intersectionWeightSum += GetGramWeight(gram, documentFrequency);
            }
        }

        if (inputWeightSum <= 0f || documentWeightSum <= 0f)
        {
            return 0f;
        }

        return (2f * intersectionWeightSum) / (inputWeightSum + documentWeightSum);
    }

    private float GetGramWeight(string gram, Dictionary<string, int> documentFrequency)
    {
        if (documentCount <= 0)
        {
            return 1f;
        }

        if (!documentFrequency.TryGetValue(gram, out var count))
        {
            count = 0;
        }

        var idf = Mathf.Log((documentCount + 1f) / (count + 1f)) + 1f;
        return Mathf.Max(0.0001f, idf);
    }

    private float ComputeLengthPenalty(int inputLength, int documentLength)
    {
        var maxLength = Mathf.Max(inputLength, documentLength);
        if (maxLength <= 0)
        {
            return 0f;
        }

        var diffRatio = Mathf.Abs(inputLength - documentLength) / (float)maxLength;
        return diffRatio * lengthPenaltyWeight;
    }

    private float ComputeSynonymSimilarity(string inputNorm, string docNorm)
    {
        if (synonymWordToGroup.Count == 0)
        {
            return 0f;
        }

        var inputGroups = CollectSynonymGroups(inputNorm);
        var docGroups = CollectSynonymGroups(docNorm);

        if (inputGroups.Count == 0 || docGroups.Count == 0)
        {
            return 0f;
        }

        var overlap = 0;
        foreach (var g in inputGroups)
        {
            if (docGroups.Contains(g))
            {
                overlap++;
            }
        }

        return (float)overlap / inputGroups.Count;
    }

    private HashSet<int> CollectSynonymGroups(string text)
    {
        var groups = new HashSet<int>();
        foreach (var kv in synonymWordToGroup)
        {
            if (text.Contains(kv.Key))
            {
                groups.Add(kv.Value);
            }
        }

        return groups;
    }

    private static HashSet<string> BuildNGrams(string text, int n)
    {
        var grams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(text))
        {
            return grams;
        }

        if (text.Length < n)
        {
            grams.Add(text);
            return grams;
        }

        for (var i = 0; i <= text.Length - n; i++)
        {
            grams.Add(text.Substring(i, n));
        }

        return grams;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var chars = new List<char>(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
            {
                continue;
            }

            chars.Add(char.ToLowerInvariant(c));
        }

        return new string(chars.ToArray());
    }

    private static AudioType GetAudioTypeFromPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".mp3":
                return AudioType.MPEG;
            case ".wav":
                return AudioType.WAV;
            case ".ogg":
                return AudioType.OGGVORBIS;
            case ".aif":
            case ".aiff":
                return AudioType.AIFF;
            default:
                return AudioType.UNKNOWN;
        }
    }

    public void StopAudioPlayback()
    {
        if (audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        CancelInvoke(nameof(OnAudioPlayFinished));
        OnAudioPlayCompleted?.Invoke();
    }

    public void AddUserMessage(string userstr)
    {
    }

    public void AddSystemMessage(string systr)
    {
    }
}