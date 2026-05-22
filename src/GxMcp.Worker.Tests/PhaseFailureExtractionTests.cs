using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // Friction 2026-05-22: when ErrorCount=0 but ExitCode!=0, the build status
    // used to read "Build Failed: 0 errors, 0 warnings". BuildService now parses
    // the raw Output for late-phase markers and surfaces a structured
    // phase_failure block plus a partial_success flag when Generation +
    // Compilation are observed as succeeded.
    public class PhaseFailureExtractionTests
    {
        [Fact]
        public void ExtractPhaseFailure_E0Marker_ReturnsLastNameAndMessage()
        {
            string output = string.Join("\n",
                "Specifying ObjA ...",
                "Generation: Sucesso",
                "Compilation: Sucesso",
                ">RO WebAppConfig",
                ">E0 WebAppConfig: O sistema não pode encontrar o arquivo especificado",
                "Build Failed");

            var info = BuildService.ExtractPhaseFailure(output);

            Assert.NotNull(info);
            Assert.Equal("WebAppConfig", info.Name);
            Assert.Contains("não pode encontrar", info.Message);
        }

        [Fact]
        public void ExtractPhaseFailure_OnlyROMarker_FallsBackToRunningStep()
        {
            string output = string.Join("\n",
                "Specifying ObjA ...",
                ">RO Copying Module GeneXus",
                "Build Failed");

            var info = BuildService.ExtractPhaseFailure(output);

            Assert.NotNull(info);
            Assert.Equal("Copying Module GeneXus", info.Name);
            Assert.False(string.IsNullOrEmpty(info.Message));
        }

        [Fact]
        public void ExtractPhaseFailure_NoMarkers_ReturnsNull()
        {
            string output = "Just plain text.\nNothing interesting here.";
            Assert.Null(BuildService.ExtractPhaseFailure(output));
        }

        [Fact]
        public void ExtractPhaseFailure_NullOrEmpty_ReturnsNull()
        {
            Assert.Null(BuildService.ExtractPhaseFailure(null));
            Assert.Null(BuildService.ExtractPhaseFailure(string.Empty));
        }

        [Fact]
        public void DidGenerationAndCompilationSucceed_BothPresent_ReturnsTrue()
        {
            string output = string.Join("\n",
                "Specifying ObjA ...",
                "Generation: Sucesso",
                "Specifying ObjB ...",
                "Compilation: Sucesso",
                ">RO WebAppConfig",
                "Build Failed");

            Assert.True(BuildService.DidGenerationAndCompilationSucceed(output));
        }

        [Fact]
        public void DidGenerationAndCompilationSucceed_EnglishLocale_AlsoMatches()
        {
            string output = "Generation: succeeded\nCompilation succeeded\n";
            Assert.True(BuildService.DidGenerationAndCompilationSucceed(output));
        }

        [Fact]
        public void DidGenerationAndCompilationSucceed_MissingOne_ReturnsFalse()
        {
            string output = "Generation: Sucesso\n>RO Compilation: failed\n";
            Assert.False(BuildService.DidGenerationAndCompilationSucceed(output));
        }
    }
}
