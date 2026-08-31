using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // Integration tests pinning the mod<->game contract against the installed
    // com.uboat.game.dll (metadata-only reflection, nothing is executed).
    // These are the G1-G4 guard rails: if the game updates and renames/removes
    // members RadioStoryIntegration.cs depends on, these tests go red.
    //
    // Game discovery: $UBOAT_GAME_MANAGED or the default Steam path.
    // No game found -> SkipTestException -> reported as SKIP by the custom runner.
    public class GameContractTests
    {
        private const string DefaultManagedDir =
            @"C:\Program Files (x86)\Steam\steamapps\common\UBOAT\UBOAT_Data\Managed";

        private const string GameAssemblyName = "com.uboat.game.dll";

        private const string RadioStoryName = "UBOAT.Game.Scene.Stories.RadioStory";
        private const string GenericStoryName = "UBOAT.Game.Scene.Stories.GenericStory";
        private const string StoryName = "UBOAT.Game.Scene.Story";

        private static Assembly _game;
        private static bool _attempted;

        private static Assembly Game()
        {
            if (_game != null)
                return _game;
            if (_attempted)
                return null;
            _attempted = true;

            string dir = Environment.GetEnvironmentVariable("UBOAT_GAME_MANAGED");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                dir = Directory.Exists(DefaultManagedDir) ? DefaultManagedDir : null;
            if (dir == null)
                return null;

            var alc = new AssemblyLoadContext("radionav-game-contract", isCollectible: false);
            alc.Resolving += (ctx, name) =>
            {
                string path = Path.Combine(dir, name.Name + ".dll");
                return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
            };

            try
            {
                _game = alc.LoadFromAssemblyPath(Path.Combine(dir, GameAssemblyName));
            }
            catch
            {
                _game = null;
            }
            return _game;
        }

        private static void RequireGame()
        {
            if (Game() == null)
                throw new SkipTestException(
                    "com.uboat.game.dll not found. Set UBOAT_GAME_MANAGED or install the game at the default Steam path.");
        }

        private static Type FindStoryType(string fullName)
        {
            return Game().GetType(fullName, false);
        }

        // Mirrors RadioStoryIntegration.FindMethodDeep: GetMethod does not trace
        // base types for NonPublic members, so walk the hierarchy explicitly.
        private static MethodInfo FindMethodDeep(Type start, string name, Type[] argTypes)
        {
            for (Type t = start; t != null; t = t.BaseType)
            {
                var m = argTypes == null
                    ? t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
                    : t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance, null, argTypes, null);
                if (m != null)
                    return m;
            }
            return null;
        }

        [Fact]
        public void RadioStory_Hierarchy_Is_RadioStory_GenericStory_Story()
        {
            RequireGame();
            var radioStory = FindStoryType(RadioStoryName);
            var genericStory = FindStoryType(GenericStoryName);
            var story = FindStoryType(StoryName);

            Assert.NotNull(radioStory);
            Assert.NotNull(genericStory);
            Assert.NotNull(story);
            Assert.Same(genericStory, radioStory.BaseType);
            Assert.Same(story, genericStory.BaseType);
        }

        [Fact]
        public void RadioStory_HasInsertDecision_AndDecisionParam_OnHierarchy()
        {
            RequireGame();
            var radioStory = FindStoryType(RadioStoryName);

            var insert = FindMethodDeep(radioStory, "InsertDecision", null);
            Assert.NotNull(insert);
            var pars = insert.GetParameters();
            Assert.Equal(2, pars.Length);
            Assert.Equal(typeof(int), pars[0].ParameterType);

            var decisionType = pars[1].ParameterType;
            Assert.NotNull(decisionType);
            Assert.Equal("UBOAT.Game.Scene.Story+Decision", decisionType.FullName);
        }

        [Fact]
        public void RadioStory_HasOnUpdated_OnHierarchy()
        {
            RequireGame();
            var radioStory = FindStoryType(RadioStoryName);

            var onUpdated = FindMethodDeep(radioStory, "OnUpdated", Type.EmptyTypes);
            Assert.NotNull(onUpdated);
        }

        [Fact]
        public void Decision_LocalizationKey_IsPublicField_NotProperty()
        {
            RequireGame();
            var radioStory = FindStoryType(RadioStoryName);
            var insert = FindMethodDeep(radioStory, "InsertDecision", null);
            var decisionType = insert.GetParameters()[1].ParameterType;

            var field = decisionType.GetField("LocalizationKey", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            Assert.Equal(typeof(string), field.FieldType);

            var prop = decisionType.GetProperty("LocalizationKey");
            Assert.Null(prop);
        }

        [Fact]
        public void RadioStory_HasStageField_AsEnumType_NotInt()
        {
            RequireGame();
            var radioStory = FindStoryType(RadioStoryName);

            // G1 corrected: "stage" exists (RadioStory.cs:83) but is typed
            // RadioStoryStage (enum). The mod reads it via Convert.ToInt32,
            // which works on boxed enums; a plain (int) cast would throw
            // InvalidCastException. MainChoices must equal 1.
            var stage = radioStory.GetField("stage", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(stage);
            Assert.True(stage.FieldType.IsEnum, "stage must be the RadioStoryStage enum");

            object mainChoices = Enum.Parse(stage.FieldType, "MainChoices");
            Assert.Equal(1, Convert.ToInt32(mainChoices));
        }

        [Fact]
        public void Decisions_Property_IsFindableFromBaseType()
        {
            RequireGame();
            var radioStory = FindStoryType(RadioStoryName);

            // RadioStoryIntegration resolves decisionsProp on RadioStory.BaseType
            // (= GenericStory); GetProperty traces base types, so this must find
            // the inherited Decisions (Story.cs:186).
            var decisions = radioStory.BaseType.GetProperty("Decisions",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(decisions);
            Assert.Equal(typeof(System.Collections.ObjectModel.ReadOnlyCollection<>).MakeGenericType(
                FindStoryType(StoryName).GetNestedType("Decision", BindingFlags.Public)),
                decisions.PropertyType);
        }

        [Fact]
        public void Decision_Ctor_SelectedByMod_IsCompatibleWithCreateDecision()
        {
            RequireGame();
            var radioStory = FindStoryType(RadioStoryName);
            var insert = FindMethodDeep(radioStory, "InsertDecision", null);
            var decisionType = insert.GetParameters()[1].ParameterType;

            // CreateDecision picks the first public ctor with pars.Length >= 6,
            // pars[0] == string and pars[3] == Action, then passes:
            //   [0]=localeKey(string) [1]=null [2]=true [3]=ToggleRadio(Action)
            //   [4]=0 [5]=null [i>=6] object[]->Array.Empty / string->null /
            //   value type->Activator / reference->null
            var ctor = decisionType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(c =>
                {
                    var p = c.GetParameters();
                    return p.Length >= 6 && p[0].ParameterType == typeof(string)
                        && p[3].ParameterType == typeof(Action);
                });

            Assert.NotNull(ctor);
            var pars = ctor.GetParameters();

            Assert.Equal(typeof(string), pars[0].ParameterType);
            Assert.Equal(typeof(string), pars[1].ParameterType);
            Assert.Equal(typeof(bool), pars[2].ParameterType);
            Assert.Equal(typeof(Action), pars[3].ParameterType);
            Assert.Equal(typeof(int), pars[4].ParameterType);
            Assert.False(pars[5].ParameterType.IsValueType, "costs must be a reference type (null is passed)");

            // Current game ctor (Story.cs:57):
            // (string localizationKey, string icon, bool enabled, Action callback,
            //  int index, IRewardInfo[] costs, params object[] parameters)
            // CreateDecision fills index 0..5 literally and i>=6 per type (object[] -> Array.Empty).
            Assert.Equal(7, pars.Length);
            Assert.Equal(typeof(object[]), pars[6].ParameterType);
        }

        [Fact]
        public void PlayerShipPosition_Chain_Exists()
        {
            RequireGame();

            // F200: GamePlayerPositionProvider resolves the player ship through
            // Entity.playerShip (static, non-public) -> PlayerShip.SandboxPlayerShip
            // -> SandboxGroup.Position (world units). Pin the chain against the
            // installed DLL so a game update cannot silently break the fix feed.
            var entity = Game().GetType("UBOAT.Game.Scene.Entities.Entity", false);
            Assert.NotNull(entity);
            var playerShipField = entity.GetField("playerShip",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(playerShipField);
            Assert.Equal("UBOAT.Game.Scene.Entities.PlayerShip", playerShipField.FieldType.FullName);

            var playerShip = playerShipField.FieldType;
            var sandbox = playerShip.GetProperty("SandboxPlayerShip",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(sandbox);
            Assert.Equal("UBOAT.Game.Sandbox.SandboxPlayerWolfpack", sandbox.PropertyType.FullName);

            var position = sandbox.PropertyType.GetProperty("Position",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(position);
            Assert.Equal("UnityEngine.Vector2", position.PropertyType.FullName);
        }

        [Fact]
        public void ShipControlUI_ChronometerBridge_Targets_Exist()
        {
            RequireGame();

            // CH-2: ShipControlUIBridge reflects these string names against the
            // native ShipControlUI/CursorManager surfaces. Pin them so a game
            // update cannot silently break the F7 chronometer overlay (RN0010).
            var ui = Game().GetType("UBOAT.Game.UI.ShipControlUI", false);
            Assert.NotNull(ui);

            Assert.NotNull(ui.GetField("chronometerToolDragContainer",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(ui.GetField("chronometerButton",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(ui.GetField("chronometerToolDefaultPos",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(ui.GetField("chronometerToolDragState",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(ui.GetField("uiDialsDataHolder",
                BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(ui.GetMethod("ApplyToolPositionLimits",
                BindingFlags.Static | BindingFlags.NonPublic));

            var cursorManager = Game().GetType("UBOAT.Game.Core.Input.CursorManager", false);
            if (cursorManager != null)
            {
                Assert.NotNull(cursorManager.GetField("normalCursorTex",
                    BindingFlags.Instance | BindingFlags.NonPublic));
            }
        }
    }
}
