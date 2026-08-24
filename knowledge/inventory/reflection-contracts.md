# Reflection contract snapshot
## Mod-side lookups (Source/)

- type `DWS.Common.InjectionFramework.ScriptableObjectSingleton` in RadioStoryIntegration.cs
- type `UBOAT.Game.Core.Data.Locale` in RadioStoryIntegration.cs
- lookup `LocalizationKey` in StoryDecisionGateway.cs
- lookup `Count` in StoryDecisionGateway.cs
- lookup `Item` in StoryDecisionGateway.cs
- lookup `ChronometerPosOverride` in ChronometerOverlayHandler.cs

## Game-side declaration snapshot (first hits)

### InsertDecision
- `UBOAT.Game.Scene\Story.cs:377`
- `UBOAT.Game.Scene.Stories\RadioStory.cs:317`
- `UBOAT.Game.Scene.Stories\WreckageStory.cs:92`

### OnUpdated
- `Mods\Enigma\Source\Patches\RadioStoryPatch.cs:35`
- `UBOAT.Game.Sandbox.Missions\WarCorrespondentMissionModifier.cs:85`
- `UBOAT.Game.Scene\Story.cs:438`
- `UBOAT.Game.Scene.Stories\GenericStory.cs:60`

### LocalizationKey
- `UBOAT.Game.Core.Data\ILocalizationVariants.cs:5`
- `UBOAT.Game.Core.Data\LocalizationVariants.cs:95`
- `UBOAT.Game.Sandbox.Reputation\RaiseHeadquartersTaskLimit.cs:20`
- `UBOAT.Game.Scene\BasicScriptedStory.cs:28`

### RadioStoryStage
- `UBOAT.Game.Scene.Stories\RadioStory.cs:26`

### ContinueStory
- `UBOAT.Game.Sandbox.Missions\RendezvousMission.cs:421`
- `UBOAT.Game.Sandbox.Missions\WarCorrespondentMissionModifier.cs:79`
- `UBOAT.Game.Sandbox.Missions\WeatherStationDeploymentAssignment.cs:128`
- `UBOAT.Game.Scene\BasicScriptedStory.cs:101`

### OnDecisionSelected
- `UBOAT.Game.Scene\BasicScriptedStory.cs:110`
- `UBOAT.Game.Scene\Story.cs:323`
- `UBOAT.Game.Scene.Stories\GenericStory.cs:46`
- `UBOAT.Game.Scene.Stories\RadioStory.cs:402`

### OnStopped
- `Mods\Enigma\Source\Patches\RadioStoryPatch.cs:148`
- `UBOAT.Game.Sandbox.Missions\WarCorrespondentMissionModifier.cs:165`
- `UBOAT.Game.Scene\BasicScriptedStory.cs:88`
- `UBOAT.Game.Scene\Story.cs:284`

### LoadSingleton
- `DWS.Common.InjectionFramework\InjectionFramework.cs:118`
- `DWS.Common.InjectionFramework\ScriptableObjectSingleton.cs:23`
- `DWS.Common.Tweening\Tweens.cs:180`
- `Mods\Enigma\Source\EnigmaConfig.cs:44`

