using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ChessTheBetrayal.View.Board;

namespace ChessTheBetrayal.Tests.EditMode.View.Board
{
    /// <summary>
    /// Whether the shaders this project writes will actually be in a player build.
    ///
    /// A shader reaches a build only when something in the build refers to it. The editor plays by
    /// different rules — it has the whole project loaded, so a shader fetched by name is always
    /// found there no matter what a build would contain. That gap shipped an Android build with no
    /// board highlights at all: every marker material is created at runtime, so no material asset
    /// carried the shader, nothing referred to it, and it was simply absent. On the desk it looked
    /// perfect, every single time.
    ///
    /// The rule this project settled on is that an authored shader is referenced by an asset the
    /// game scene reaches. That is what these check. A shader deliberately moved to the project's
    /// always-included list instead would need this updated — that is intended: the policy is
    /// worth stating in one place rather than discovering on a device.
    /// </summary>
    [TestFixture]
    public class ShaderReachesTheBuildTests
    {
        private const string GameScenePath = "Assets/_Scenes/Game Scene.unity";
        private const string AuthoredShaderFolder = "Assets/Material/Shaders";
        private const string PalettePath = "Assets/Material/BoardHighlightPalette.asset";

        private static List<string> AuthoredShaderPaths()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { AuthoredShaderFolder }))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            return paths;
        }

        [Test]
        public void TheGameSceneExistsWhereTheseTestsExpectIt()
        {
            // Everything below is measured against the scene, so a moved or renamed scene has to
            // fail loudly here rather than quietly turning the real checks into no-ops.
            Assert.That(AssetDatabase.LoadAssetAtPath<Object>(GameScenePath), Is.Not.Null,
                $"{GameScenePath} is gone. These tests measure against it and cannot mean anything without it.");
        }

        [Test]
        public void ThisProjectActuallyAuthorsShadersToCheck()
        {
            // A folder that has been emptied or renamed would leave the sweep below passing over
            // nothing at all, which reads exactly like every shader being fine.
            Assert.That(AuthoredShaderPaths(), Is.Not.Empty,
                $"No shaders found under {AuthoredShaderFolder} — the sweep below would be checking nothing.");
        }

        [Test]
        public void EveryShaderThisProjectWritesIsReachableFromTheGameScene()
        {
            var shipped = new HashSet<string>(AssetDatabase.GetDependencies(GameScenePath, recursive: true));

            var unreachable = new List<string>();
            foreach (string shaderPath in AuthoredShaderPaths())
            {
                if (!shipped.Contains(shaderPath)) unreachable.Add(shaderPath);
            }

            Assert.That(unreachable, Is.Empty,
                "Nothing the game scene reaches refers to these shaders, so a player build will not " +
                "contain them and whatever they draw will be missing on a device while looking correct " +
                "in the editor:\n  " + string.Join("\n  ", unreachable));
        }

        [Test]
        public void TheHighlightPaletteCarriesTheShaderItsMaterialsAreBuiltFrom()
        {
            // The specific reference the board highlights hang on. Every highlight material is made
            // from it at runtime, so if it is cleared there is nothing else in the project pointing
            // at that shader and the whole board goes unmarked on a device.
            var palette = AssetDatabase.LoadAssetAtPath<BoardHighlightPalette>(PalettePath);
            Assert.That(palette, Is.Not.Null, $"{PalettePath} is missing.");

            Assert.That(palette.HighlightShader, Is.Not.Null,
                "The palette has no shader assigned, so no square marker can be drawn.");
            Assert.That(palette.HighlightShader.name, Is.EqualTo("Chess/BoardHighlight"));
        }

        [Test]
        public void TheHighlightShaderCompiles()
        {
            // A shader present in the build but failing to compile is the same outcome as a missing
            // one, and it fails the same silent way.
            var palette = AssetDatabase.LoadAssetAtPath<BoardHighlightPalette>(PalettePath);
            Assert.That(palette, Is.Not.Null);
            Assert.That(palette.HighlightShader, Is.Not.Null);

            Assert.That(palette.HighlightShader.isSupported, Is.True,
                "The board highlight shader does not compile for this platform.");
        }
    }
}
