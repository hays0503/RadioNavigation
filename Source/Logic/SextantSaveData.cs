using UBOAT.Game.Scene.Entities;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Campaign SceneObject: the game writes this instance field to
    /// SaveData.sceneObjects and recreates it on load. Not DontDestroyOnLoad,
    /// not an asset and not UI. Do NOT add runtime references or load logic to
    /// this carrier: fields are deserialized on the game's worker thread.</summary>
    public sealed class SextantSaveData : SceneObject
    {
        public string Payload;
    }
}
