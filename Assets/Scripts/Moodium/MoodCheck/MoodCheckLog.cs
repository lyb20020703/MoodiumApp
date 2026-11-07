using System;
using System.IO;
using UnityEngine;

namespace Moodium.MoodCheck
{
    public static class MoodCheckLog
    {
        [Serializable]
        class PreLogDto
        {
            public string sessionId;
            public string visitId;
            public string phase;
            public float valence;
            public float arousal;
            public string mood;
            public float intensity;
            public string inputMethod;
            public string recommendedWorldId;
            public string enteredWorldId;
            public string timestampUtc;
            public string language;
        }

        [Serializable]
        sealed class PostLogDto : PreLogDto
        {
            public float durationInWorldSeconds;
            public int pinchCount;
        }

        public static string DefaultPath =>
            Path.Combine(Application.persistentDataPath, "moodium_mood_log.json");

        public static string BuildJsonLine(MoodCheckSample sample)
        {
            if (sample == null)
                throw new ArgumentNullException(nameof(sample));

            PreLogDto dto = sample.phase == MoodCheckPhase.Post
                ? new PostLogDto
                {
                    durationInWorldSeconds = Mathf.Max(0f, sample.durationInWorldSeconds),
                    pinchCount = Mathf.Max(0, sample.pinchCount)
                }
                : new PreLogDto();
            Populate(dto, sample);
            return JsonUtility.ToJson(dto);
        }

        public static bool TryAppend(MoodCheckSample sample, string path = null)
        {
            var target = string.IsNullOrEmpty(path) ? DefaultPath : path;
            try
            {
                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                var json = BuildJsonLine(sample);
                File.AppendAllText(target, json + "\n");
                Debug.Log($"[Mood Check] {json}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Mood Check] Could not write local mood log: {exception.Message}");
                return false;
            }
        }

        static void Populate(PreLogDto dto, MoodCheckSample sample)
        {
            dto.sessionId = sample.sessionId ?? string.Empty;
            dto.visitId = sample.visitId ?? string.Empty;
            dto.phase = sample.phase.ToString().ToLowerInvariant();
            dto.valence = Mathf.Clamp(sample.valence, -1f, 1f);
            dto.arousal = Mathf.Clamp(sample.arousal, -1f, 1f);
            dto.mood = sample.mood.ToString().ToLowerInvariant();
            dto.intensity = Mathf.Clamp01(sample.intensity);
            dto.inputMethod = sample.inputMethod.ToString().ToLowerInvariant();
            dto.recommendedWorldId = sample.recommendedWorldId ?? string.Empty;
            dto.enteredWorldId = sample.enteredWorldId ?? string.Empty;
            dto.timestampUtc = sample.timestampUtc ?? string.Empty;
            dto.language = sample.language ?? string.Empty;
        }
    }
}
