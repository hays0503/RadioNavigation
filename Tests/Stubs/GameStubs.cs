using System;

namespace UnityEngine
{
    public static class Application
    {
        public static string version => "test";
    }
}

namespace UBOAT.Game.Scene.Stories
{
    public class Story
    {
        public object Decisions { get; set; } = new System.Collections.Generic.List<object>();
        private void InsertDecision(int index, object decision) { }
        private void OnUpdated() { }
    }

    public class GenericStory : Story { }

    public class RadioStory : GenericStory
    {
        private RadioStoryStage stage = RadioStoryStage.Unknown;
    }

    public enum RadioStoryStage
    {
        Unknown = 0,
        MainChoices = 1
    }
}
