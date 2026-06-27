using AZERTYGlobal;
using Xunit;

namespace AZERTYGlobal.Tests;

public class LessonCoreTests
{
    [Fact]
    public void CatalogLoader_LoadsInitiationAndSiteLessonsInOrder()
    {
        var catalog = LessonCatalogLoader.LoadFromResource();

        Assert.Equal(8, catalog.Modules.Count);
        Assert.Equal(7, catalog.SiteModuleCount);
        Assert.Equal(31, catalog.SiteLessonCount);
        Assert.Equal(70, catalog.SiteExerciseCount);
        Assert.Equal(76, catalog.TotalExerciseCount);
        Assert.Equal("initiation", catalog.Modules[0].Id);
        Assert.Equal("email-web", catalog.Modules[1].Id);
        Assert.All(catalog.Modules.Skip(1).SelectMany(m => m.Lessons).SelectMany(l => l.Exercises),
            exercise => Assert.Equal(LessonTypingMode.Flexible, exercise.TypingMode));
        Assert.All(catalog.Modules[0].Lessons.SelectMany(l => l.Exercises),
            exercise => Assert.Equal(LessonTypingMode.Strict, exercise.TypingMode));
    }

    [Fact]
    public void ExerciseHash_ChangesWhenInstructionOrContentChanges()
    {
        var a = new LessonExercise("m", "l", 0, "practice", "Tapez", "abc", LessonTypingMode.Flexible);
        var same = new LessonExercise("m", "l", 0, "practice", "Tapez", "abc", LessonTypingMode.Flexible);
        var changedInstruction = new LessonExercise("m", "l", 0, "practice", "Tapez vite", "abc", LessonTypingMode.Flexible);
        var changedContent = new LessonExercise("m", "l", 0, "practice", "Tapez", "abcd", LessonTypingMode.Flexible);

        Assert.Equal(a.Hash, same.Hash);
        Assert.NotEqual(a.Hash, changedInstruction.Hash);
        Assert.NotEqual(a.Hash, changedContent.Hash);
        Assert.EndsWith("/" + a.Hash, a.StableKey);
    }

    [Fact]
    public void TypingSession_StrictMode_RejectsBackspaceAndCountsErrors()
    {
        var clock = new FakeClock();
        var exercise = new LessonExercise("initiation", "premiers-pas", 0, "practice", "Tape", "ab", LessonTypingMode.Strict);
        var session = new LessonTypingSession(exercise, clock.Now);

        var wrong = session.TypeChar('x');
        var backspace = session.Backspace();
        var first = session.TypeChar('a');
        clock.AdvanceSeconds(30);
        var second = session.TypeChar('b');

        Assert.True(wrong.WasError);
        Assert.False(backspace.Accepted);
        Assert.False(first.WasError);
        Assert.True(second.ExerciseCompleted);
        Assert.True(session.IsExerciseComplete);
        Assert.Equal(3, session.Stats.ProductiveKeystrokes);
        Assert.Equal(1, session.Stats.ErrorCount);
        Assert.Equal(0, session.Stats.BackspaceCount);
        Assert.Equal(new[] { 'a' }, session.Stats.GetHardestCharacters(3));
    }

    [Fact]
    public void TypingSession_StrictMode_ExposesVisibleWrongCharacter()
    {
        var exercise = new LessonExercise("initiation", "premiers-pas", 0, "practice", "Tape", "ab", LessonTypingMode.Strict);
        var session = new LessonTypingSession(exercise);

        session.TypeChar('x');
        Assert.Collection(session.GetTypedLineSnapshot(),
            item =>
            {
                Assert.Equal('x', item.Actual);
                Assert.Equal(LessonCharacterState.Wrong, item.State);
            });

        session.TypeChar('a');
        Assert.Collection(session.GetTypedLineSnapshot(),
            item =>
            {
                Assert.Equal('a', item.Actual);
                Assert.Equal(LessonCharacterState.Correct, item.State);
            });
    }

    [Fact]
    public void TypingSession_FlexibleMode_SupportsBackspaceAndMultilineProgress()
    {
        var clock = new FakeClock();
        var exercise = new LessonExercise("m", "l", 0, "practice", "Tape", "ab\ncd", LessonTypingMode.Flexible);
        var session = new LessonTypingSession(exercise, clock.Now);

        session.TypeChar('a');
        session.TypeChar('x');
        session.Backspace();
        var lineDone = session.TypeChar('b');
        var enter = session.TypeChar('\n');
        Assert.True(lineDone.LineCompleted);
        Assert.False(lineDone.ExerciseCompleted);
        Assert.False(enter.Accepted);

        Assert.True(session.AdvanceCompletedLine());
        session.TypeChar('c');
        clock.AdvanceSeconds(30);
        var completed = session.TypeChar('d');

        Assert.True(completed.ExerciseCompleted);
        Assert.Equal(5, session.Stats.ProductiveKeystrokes);
        Assert.Equal(1, session.Stats.BackspaceCount);
        Assert.Equal(1, session.Stats.ErrorCount);
        Assert.Equal(80, session.Stats.AccuracyPercent);
    }

    [Fact]
    public void TypingSession_FlexibleMode_ExposesTypedCharactersAndBackspace()
    {
        var exercise = new LessonExercise("m", "l", 0, "practice", "Tape", "ab", LessonTypingMode.Flexible);
        var session = new LessonTypingSession(exercise);

        session.TypeChar('a');
        session.TypeChar('x');

        Assert.Collection(session.GetTypedLineSnapshot(),
            first =>
            {
                Assert.Equal('a', first.Actual);
                Assert.Equal(LessonCharacterState.Correct, first.State);
            },
            second =>
            {
                Assert.Equal('x', second.Actual);
                Assert.Equal(LessonCharacterState.Wrong, second.State);
            });

        session.Backspace();
        Assert.Collection(session.GetTypedLineSnapshot(),
            item =>
            {
                Assert.Equal('a', item.Actual);
                Assert.Equal(LessonCharacterState.Correct, item.State);
            });
    }

    [Fact]
    public void TypingSession_FlexibleMode_ReportsBackspaceCorrectionAfterWrongCharacter()
    {
        var exercise = new LessonExercise("m", "l", 0, "practice", "Tape", "ab", LessonTypingMode.Flexible);
        var session = new LessonTypingSession(exercise);

        Assert.False(session.NeedsBackspaceCorrection);

        session.TypeChar('x');
        Assert.True(session.NeedsBackspaceCorrection);

        session.Backspace();
        Assert.False(session.NeedsBackspaceCorrection);

        session.TypeChar('a');
        Assert.False(session.NeedsBackspaceCorrection);
    }

    [Fact]
    public void TypingSession_ResetExercise_ClearsAttemptStats()
    {
        var clock = new FakeClock();
        var exercise = new LessonExercise("m", "l", 0, "practice", "Tape", "ab", LessonTypingMode.Flexible);
        var session = new LessonTypingSession(exercise, clock.Now);

        session.TypeChar('x');
        session.Backspace();
        session.RecordHint();

        Assert.Equal(1, session.Stats.ProductiveKeystrokes);
        Assert.Equal(1, session.Stats.ErrorCount);
        Assert.Equal(1, session.Stats.BackspaceCount);
        Assert.Equal(1, session.Stats.HintCount);

        session.ResetExercise();

        Assert.Equal(0, session.LineIndex);
        Assert.Equal(0, session.CursorPosition);
        Assert.False(session.IsExerciseComplete);
        Assert.Equal(0, session.Stats.ProductiveKeystrokes);
        Assert.Equal(0, session.Stats.ErrorCount);
        Assert.Equal(0, session.Stats.BackspaceCount);
        Assert.Equal(0, session.Stats.HintCount);
        Assert.Null(session.Stats.StartedAt);
    }

    [Fact]
    public void ProgressStore_InvalidatesExerciseWhenHashChanges()
    {
        string path = Path.Combine(Path.GetTempPath(), "azerty-lessons-test-" + Guid.NewGuid() + ".json");
        try
        {
            var clock = new FakeClock();
            var exercise = new LessonExercise("m", "l", 0, "practice", "Tape", "abc", LessonTypingMode.Flexible);
            var changed = new LessonExercise("m", "l", 0, "practice", "Tape", "abcd", LessonTypingMode.Flexible);
            var session = new LessonTypingSession(exercise, clock.Now);
            session.TypeChar('a');
            session.TypeChar('b');
            clock.AdvanceSeconds(30);
            session.TypeChar('c');

            var store = new LessonProgressStore(path);
            store.RecordSuccess(exercise, session.Stats);

            var reloaded = new LessonProgressStore(path);
            Assert.True(reloaded.IsCompleted(exercise));
            Assert.False(reloaded.IsCompleted(changed));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ProgressStore_SyncsOnboardingAsNeutralInitiationProgress()
    {
        string path = Path.Combine(Path.GetTempPath(), "azerty-lessons-test-" + Guid.NewGuid() + ".json");
        try
        {
            var catalog = LessonCatalogLoader.LoadFromResource();
            var store = new LessonProgressStore(path);

            store.SyncOnboardingProgress(catalog, 3);

            var initiation = catalog.Modules[0].Lessons[0].Exercises;
            Assert.True(store.IsCompleted(initiation[0]));
            Assert.True(store.IsCompleted(initiation[1]));
            Assert.True(store.IsCompleted(initiation[2]));
            Assert.False(store.IsCompleted(initiation[3]));
            var progress = store.GetValidProgress(initiation[0]);
            Assert.NotNull(progress);
            Assert.Equal(1, progress.SuccessfulAttempts);
            Assert.Null(progress.BestWpm);
            Assert.Null(progress.LastCompletedUtc);
            Assert.Equal(0, progress.HintsUsed);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ProgressStore_ResetPreventsImmediateOnboardingReimport()
    {
        string path = Path.Combine(Path.GetTempPath(), "azerty-lessons-test-" + Guid.NewGuid() + ".json");
        try
        {
            var catalog = LessonCatalogLoader.LoadFromResource();
            var store = new LessonProgressStore(path);
            store.SyncOnboardingProgress(catalog, 3);
            Assert.Equal(3, store.CountCompleted(catalog));

            store.ResetAll(onboardingSyncedMaxStep: 3);
            store.SyncOnboardingProgress(catalog, 3);

            Assert.Equal(0, store.CountCompleted(catalog));
            Assert.Equal(3, store.OnboardingSyncedMaxStep);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ProgressStore_DoesNotPersistAbandonedAttempt()
    {
        string path = Path.Combine(Path.GetTempPath(), "azerty-lessons-test-" + Guid.NewGuid() + ".json");
        try
        {
            var exercise = new LessonExercise("m", "l", 0, "practice", "Tape", "abc", LessonTypingMode.Flexible);
            var session = new LessonTypingSession(exercise);
            session.TypeChar('a');
            session.TypeChar('b');

            var reloaded = new LessonProgressStore(path);

            Assert.False(reloaded.IsCompleted(exercise));
            Assert.Empty(reloaded.Exercises);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ProgressStore_DoesNotPersistTypedErrorCharacters()
    {
        string path = Path.Combine(Path.GetTempPath(), "azerty-lessons-test-" + Guid.NewGuid() + ".json");
        try
        {
            var exercise = new LessonExercise("module", "lesson", 0, "practice", "Tapez ab", "ab", LessonTypingMode.Strict);
            var session = new LessonTypingSession(exercise);
            session.TypeChar('x');
            session.TypeChar('a');
            session.TypeChar('b');

            var store = new LessonProgressStore(path);
            store.RecordSuccess(exercise, session.Stats);

            string json = File.ReadAllText(path);
            Assert.DoesNotContain("errorMatrix", json);
            Assert.DoesNotContain("\"x\"", json);
            Assert.True(store.IsCompleted(exercise));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ProgressStore_RemovesLegacyErrorMatrixOnLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), "azerty-lessons-test-" + Guid.NewGuid() + ".json");
        try
        {
            var exercise = new LessonExercise("module", "lesson", 0, "practice", "Tapez ab", "ab", LessonTypingMode.Strict);
            string json = "{\n" +
                "  \"version\": 1,\n" +
                "  \"lastModuleId\": \"module\",\n" +
                "  \"lastLessonId\": \"lesson\",\n" +
                "  \"lastExerciseIndex\": 0,\n" +
                "  \"onboardingSyncedMaxStep\": 0,\n" +
                "  \"exercises\": {\n" +
                $"    \"{exercise.StableKey}\": {{\n" +
                $"      \"hash\": \"{exercise.Hash}\",\n" +
                "      \"completed\": true,\n" +
                "      \"successfulAttempts\": 1,\n" +
                "      \"bestWpm\": null,\n" +
                "      \"bestAccuracyPercent\": null,\n" +
                "      \"bestSeconds\": null,\n" +
                "      \"lastCompletedUtc\": null,\n" +
                "      \"hintsUsed\": 0,\n" +
                "      \"errorMatrix\": { \"a\": { \"x\": 1 } }\n" +
                "    }\n" +
                "  }\n" +
                "}\n";
            File.WriteAllText(path, json);

            var store = new LessonProgressStore(path);

            string cleaned = File.ReadAllText(path);
            Assert.True(store.IsCompleted(exercise));
            Assert.DoesNotContain("errorMatrix", cleaned);
            Assert.DoesNotContain("\"x\"", cleaned);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ProgressStore_KeepsBestScoresAcrossReplays()
    {
        string path = Path.Combine(Path.GetTempPath(), "azerty-lessons-test-" + Guid.NewGuid() + ".json");
        try
        {
            var exercise = new LessonExercise("m", "l", 0, "practice", "Tape", "abcdefghij", LessonTypingMode.Flexible);
            var store = new LessonProgressStore(path);

            var clock = new FakeClock();
            var first = new LessonTypingSession(exercise, clock.Now);
            TypeText(first, "abcdefghi");
            clock.AdvanceSeconds(60);
            first.TypeChar('j');
            store.RecordSuccess(exercise, first.Stats);

            var second = new LessonTypingSession(exercise, clock.Now);
            second.TypeChar('a');
            second.TypeChar('x');
            second.Backspace();
            TypeText(second, "bcdefghi");
            clock.AdvanceSeconds(30);
            second.TypeChar('j');
            store.RecordSuccess(exercise, second.Stats);

            var progress = store.GetValidProgress(exercise);
            Assert.NotNull(progress);
            Assert.Equal(2, progress.SuccessfulAttempts);
            Assert.Equal(4, progress.BestWpm);
            Assert.Equal(100, progress.BestAccuracyPercent);
            Assert.Equal(30, progress.BestSeconds);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ProgressStore_RollsBackMemoryWhenSaveFails()
    {
        string path = Path.Combine(Path.GetTempPath(), "azerty-lessons-dir-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        try
        {
            var exercise = new LessonExercise("m", "l", 0, "practice", "Tape", "ab", LessonTypingMode.Flexible);
            var session = new LessonTypingSession(exercise);
            session.TypeChar('a');
            session.TypeChar('b');

            var store = new LessonProgressStore(path);
            store.RecordSuccess(exercise, session.Stats);

            Assert.False(store.IsCompleted(exercise));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Fact]
    public void ProgressStore_DoesNotOverwriteExistingProgress_WhenLoadFailed()
    {
        string path = Path.Combine(Path.GetTempPath(), "azerty-lessons-test-" + Guid.NewGuid() + ".json");
        const string invalidJson = "{ invalid";
        try
        {
            File.WriteAllText(path, invalidJson);
            var exercise = new LessonExercise("m", "l", 0, "practice", "Tape", "ab", LessonTypingMode.Flexible);
            var session = new LessonTypingSession(exercise);
            session.TypeChar('a');
            session.TypeChar('b');

            var store = new LessonProgressStore(path);
            store.RecordSuccess(exercise, session.Stats);

            Assert.Equal(invalidJson, File.ReadAllText(path));
            Assert.False(store.IsCompleted(exercise));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SyncScript_AllowsCreatingPublicLessonsResource()
    {
        string root = FindMicrosoftStoreRoot();
        string script = File.ReadAllText(Path.Combine(root, "scripts", "Sync-LayoutResources.ps1"));

        Assert.Contains("[switch]$AllowCreate", script);
        Assert.Contains("src\\lessons.json') -AllowCreate", script);
    }

    [Fact]
    public void KeyboardRenderer_LessonProfileUsesOnboardingBaseAndRevealsLessonCharacters()
    {
        Assert.True(KeyboardRenderer.IsSlotVisible(
            KeyboardRenderProfile.Lesson,
            scancode: 0x29,
            layer: 0,
            value: "@"));

        Assert.True(KeyboardRenderer.IsSlotVisible(
            KeyboardRenderProfile.Lesson,
            scancode: 0x12,
            layer: 2,
            value: "€"));

        Assert.True(KeyboardRenderer.IsSlotVisible(
            KeyboardRenderProfile.Lesson,
            scancode: 0x1A,
            layer: 2,
            value: "dk_caron",
            lessonVisibleCharacters: new HashSet<string> { "dk:caron" }));

        Assert.False(KeyboardRenderer.IsSlotVisible(
            KeyboardRenderProfile.Lesson,
            scancode: 0x1A,
            layer: 2,
            value: "dk_caron"));

        Assert.True(KeyboardRenderer.IsSlotVisible(
            KeyboardRenderProfile.Lesson,
            scancode: 0x05,
            layer: 2,
            value: "’",
            lessonVisibleCharacters: new HashSet<string> { "’" }));
    }

    [Fact]
    public void KeyboardRenderer_DisplayInvisible_DoesNotMarkRegularSpace()
    {
        Assert.Equal(" ", KeyboardRenderer.DisplayInvisible(" "));
        Assert.Equal("esp. ins. fine", KeyboardRenderer.DisplayInvisible("\u202F"));
        Assert.Equal("esp. ins.", KeyboardRenderer.DisplayInvisible("\u00A0"));
    }

    [Fact]
    public void LessonHintProvider_ExposesDeadKeyActivationStep()
    {
        var provider = new LessonHintProvider();

        var method = provider.GetRecommendedMethod('©');

        Assert.NotNull(method);
        Assert.True(method!.IsDeadKey);
        Assert.Equal("dk_misc_symbols", method.DeadKey);
        Assert.Equal("KeyC", method.Key);
        Assert.Equal("Base", method.Layer);
        Assert.Equal("Backquote", method.DkActivationKey);
        Assert.Equal("AltGr", method.DkActivationLayer);
        Assert.Equal("dk:misc_symbols", method.DeadKeyToken);
        Assert.Equal(new LessonHintKeyStep("Backquote", "AltGr"), LessonHintProvider.GetCurrentStep(method, activeDeadKey: null));
        Assert.Equal(new LessonHintKeyStep("KeyC", "Base"), LessonHintProvider.GetCurrentStep(method, activeDeadKey: "dk_misc_symbols"));
    }

    [Fact]
    public void LessonHintProvider_RevealsDeadKeysRequiredByLessonCharacters()
    {
        var provider = new LessonHintProvider();
        var visible = new HashSet<string>(StringComparer.Ordinal);

        provider.AddRequiredCharacters("© ® ™", visible);

        Assert.Contains("©", visible);
        Assert.Contains("dk:misc_symbols", visible);
        Assert.True(KeyboardRenderer.IsSlotVisible(
            KeyboardRenderProfile.Lesson,
            scancode: 0x29,
            layer: 2,
            value: "dk_misc_symbols",
            lessonVisibleCharacters: visible));
    }

    private static void TypeText(LessonTypingSession session, string text)
    {
        foreach (char c in text)
            session.TypeChar(c);
    }

    private static string FindMicrosoftStoreRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "scripts", "Sync-LayoutResources.ps1");
            if (File.Exists(candidate))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Racine Microsoft Store introuvable depuis les tests.");
    }

    private sealed class FakeClock
    {
        private DateTimeOffset _now = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Now() => _now;
        public void AdvanceSeconds(int seconds) => _now = _now.AddSeconds(seconds);
    }
}
