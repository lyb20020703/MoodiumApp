using System;

namespace Moodium
{
    /// <summary>
    /// visionOS may append an anchor UUID to the reference image name reported by
    /// ARFoundation. This keeps routing based on the authored library name stable.
    /// </summary>
    public static class MoodiumImageReferenceName
    {
        public static bool Matches(string detectedName, string authoredName)
        {
            if (string.IsNullOrEmpty(detectedName) || string.IsNullOrEmpty(authoredName))
                return false;

            return string.Equals(detectedName, authoredName, StringComparison.Ordinal) ||
                   detectedName.StartsWith(authoredName + "_", StringComparison.Ordinal);
        }
    }
}
