using System;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // Thrown by integration tests when the game is not installed locally.
    // The custom runner (Tests/Runner) reports this as SKIP, not FAIL.
    public class SkipTestException : Exception
    {
        public SkipTestException(string message) : base(message)
        {
        }
    }
}
