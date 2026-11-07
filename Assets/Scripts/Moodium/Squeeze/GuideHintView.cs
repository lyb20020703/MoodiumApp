using System.Collections.Generic;
namespace Moodium.Squeeze
{
    public sealed class GuideHintView
    {
        readonly HashSet<string> shown=new();
        readonly HashSet<string> once=new();
        public string[] Shown=>new List<string>(shown).ToArray();
        public int Starts,ShortHolds,Rapid;
        public void Reset(){once.Clear();Starts=ShortHolds=Rapid=0;}
        public string Show(string key){if(key!=null)shown.Add(key);return key;}
        public string Once(string key){if(!once.Add(key))return null;return Show(key);}
    }
}
