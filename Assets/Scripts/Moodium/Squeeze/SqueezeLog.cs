using System;
using System.IO;
using UnityEngine;
namespace Moodium.Squeeze
{
    public sealed class SqueezeLog
    {
        [Serializable] public sealed class Entry
        {
            public string type,visitId,world,mode,from,to;
            public double tStart,holdSeconds,gapSeconds,closure,beatError,breathQuality,exhaleQuality,energyGained,assistMult,energy;
            public bool closureAvailable,adaptiveModeUsed;
            public int combo,squeezeCount,breathCount;
            public double timeTo100,avgBeatAccuracy,avgBreathCycleSeconds;
            public string[] modesUsed,hintsShown;
            public double[] timeInMode;
        }
        readonly string path;
        readonly bool enabled;
        public SqueezeLog(string visitId,bool enabled)
        {
            // Session IDs are generated locally, never accept arbitrary paths.
            var safe=System.Text.RegularExpressions.Regex.Replace(visitId??Guid.NewGuid().ToString("N"),"[^a-zA-Z0-9_-]","");
            path=Path.Combine(Application.persistentDataPath,"squeeze-"+safe+".jsonl");this.enabled=enabled;
        }
        public void Write(Entry entry)
        {
            if(!enabled)return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(path));File.AppendAllText(path,JsonUtility.ToJson(entry)+"\n"); }
            catch(Exception e){Debug.LogWarning("[Squeeze Log] Local log unavailable: "+e.Message);}
        }
    }
}
