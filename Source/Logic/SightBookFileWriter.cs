using System;
using System.IO;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Event-driven export. No Unity dependency; never runs per frame.</summary>
    internal static class SightBookFileWriter
    {
        internal const string FileName = "sight-book.txt";

        internal static bool TryWrite(string targetDirectory, string payload,
            out string path, out string error)
        {
            path = null;
            error = null;
            if (string.IsNullOrEmpty(targetDirectory))
            {
                error = "No target directory";
                return false;
            }
            try
            {
                Directory.CreateDirectory(targetDirectory);
                string target = Path.Combine(targetDirectory, FileName);
                string crlf = payload.Replace("\r\n", "\n").Replace("\n", "\r\n");
                File.WriteAllText(target, crlf, new UTF8Encoding(true));
                path = target;
                return true;
            }
            catch (IOException e)
            {
                error = e.Message;
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
