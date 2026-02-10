using System;

namespace RXM.Utils
{
    public static class Truncate
    {
        public static string TruncateText(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
            {
                // Console.WriteLine("[Truncate] Input is bad");
                return "";
            }

            if (text.Length <= max)
            {
                //   Console.WriteLine("Dont need to truncate");
                return text;
            }
            if (max <= 2)
            {
                var Short = text.Substring(0, Math.Min(text.Length, max));
                return Short;
            }
            var result = text.Substring(0, max - 2) + "..";
            //   Console.WriteLine($"Truncated {result.Length} characters");
            return result;
        }
    }
}
