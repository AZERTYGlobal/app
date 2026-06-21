namespace AZERTYGlobal;

internal enum LessonCharacterState
{
    Pending,
    Current,
    Correct,
    Wrong
}

internal readonly record struct LessonCharacterSnapshot(char Expected, LessonCharacterState State);
internal readonly record struct LessonTypedCharacterSnapshot(char Actual, LessonCharacterState State);

internal readonly record struct LessonInputResult(
    bool Accepted,
    bool WasError,
    char? Expected,
    char? Actual,
    bool LineCompleted,
    bool ExerciseCompleted);

internal sealed class LessonAttemptStats
{
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _completedAt;
    private readonly Dictionary<char, Dictionary<char, int>> _errorMatrix = new();

    public LessonAttemptStats(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int ProductiveKeystrokes { get; private set; }
    public int CorrectKeystrokes { get; private set; }
    public int ErrorCount { get; private set; }
    public int BackspaceCount { get; private set; }
    public int HintCount { get; private set; }
    public DateTimeOffset? StartedAt => _startedAt;
    public DateTimeOffset? CompletedAt => _completedAt;
    public IReadOnlyDictionary<char, Dictionary<char, int>> ErrorMatrix => _errorMatrix;

    public void RecordChar(char actual, char? expected)
    {
        _startedAt ??= _clock();
        ProductiveKeystrokes++;
        if (expected.HasValue && actual == expected.Value)
        {
            CorrectKeystrokes++;
            return;
        }

        ErrorCount++;
        if (expected.HasValue)
        {
            if (!_errorMatrix.TryGetValue(expected.Value, out var wrongs))
            {
                wrongs = new Dictionary<char, int>();
                _errorMatrix[expected.Value] = wrongs;
            }
            wrongs[actual] = wrongs.TryGetValue(actual, out int count) ? count + 1 : 1;
        }
    }

    public void RecordBackspace()
    {
        BackspaceCount++;
    }

    public void RecordHint()
    {
        HintCount++;
    }

    public void Complete()
    {
        _completedAt ??= _clock();
    }

    public double ElapsedSeconds
    {
        get
        {
            if (!_startedAt.HasValue) return 0;
            var end = _completedAt ?? _clock();
            return Math.Max(0, (end - _startedAt.Value).TotalSeconds);
        }
    }

    public int? Wpm
    {
        get
        {
            double elapsedMinutes = ElapsedSeconds / 60d;
            if (ProductiveKeystrokes < 10 || elapsedMinutes <= 0) return null;
            return (int)Math.Round((ProductiveKeystrokes / 5d) / elapsedMinutes);
        }
    }

    public int? AccuracyPercent
    {
        get
        {
            if (ProductiveKeystrokes <= 0) return null;
            return (int)Math.Round(100d * CorrectKeystrokes / ProductiveKeystrokes);
        }
    }

    public IReadOnlyList<char> GetHardestCharacters(int maxCount)
    {
        return _errorMatrix
            .OrderByDescending(pair => pair.Value.Values.Sum())
            .ThenBy(pair => pair.Key)
            .Take(maxCount)
            .Select(pair => pair.Key)
            .ToArray();
    }
}

internal sealed class LessonTypingSession
{
    private readonly string[] _lines;
    private readonly Func<DateTimeOffset>? _clock;
    private readonly List<bool> _flexibleCorrectStates = new();
    private string _typedLine = "";
    private bool _strictCurrentWrong;
    private char? _strictWrongCharacter;

    public LessonTypingSession(LessonExercise exercise, Func<DateTimeOffset>? clock = null)
    {
        Exercise = exercise;
        Mode = exercise.TypingMode;
        _lines = exercise.Lines;
        _clock = clock;
        Stats = new LessonAttemptStats(_clock);
        ResetLineState();
    }

    public LessonExercise Exercise { get; }
    public LessonTypingMode Mode { get; }
    public LessonAttemptStats Stats { get; private set; }
    public int LineIndex { get; private set; }
    public int CursorPosition { get; private set; }
    public bool IsLineComplete { get; private set; }
    public bool IsExerciseComplete { get; private set; }
    public string CurrentLine => _lines[Math.Min(LineIndex, _lines.Length - 1)];
    public string TypedLine => _typedLine;
    public int TotalLines => _lines.Length;

    public LessonInputResult TypeChar(char c)
    {
        if (IsExerciseComplete || IsLineComplete) return IgnoredResult();
        if (c == '\r' || c == '\n') return IgnoredResult();

        return Mode == LessonTypingMode.Strict ? TypeStrict(c) : TypeFlexible(c);
    }

    public LessonInputResult Backspace()
    {
        if (IsExerciseComplete || IsLineComplete) return IgnoredResult();
        if (Mode == LessonTypingMode.Strict) return IgnoredResult();
        if (_typedLine.Length == 0) return IgnoredResult();

        Stats.RecordBackspace();
        _typedLine = _typedLine[..^1];
        RecomputeFlexibleStates();
        CursorPosition = _typedLine.Length;
        return new LessonInputResult(true, false, null, null, false, false);
    }

    public void ResetExercise()
    {
        LineIndex = 0;
        CursorPosition = 0;
        IsLineComplete = false;
        IsExerciseComplete = false;
        _typedLine = "";
        _strictCurrentWrong = false;
        _strictWrongCharacter = null;
        Stats = new LessonAttemptStats(_clock);
        ResetLineState();
    }

    public bool AdvanceCompletedLine()
    {
        if (!IsLineComplete || IsExerciseComplete) return false;
        if (LineIndex >= _lines.Length - 1)
        {
            CompleteExercise();
            return true;
        }

        LineIndex++;
        CursorPosition = 0;
        IsLineComplete = false;
        _typedLine = "";
        _strictCurrentWrong = false;
        _strictWrongCharacter = null;
        ResetLineState();
        return true;
    }

    public char? GetNextExpectedCharacter()
    {
        if (IsExerciseComplete || IsLineComplete) return null;
        string line = CurrentLine;
        if (CursorPosition >= 0 && CursorPosition < line.Length)
            return line[CursorPosition];
        return null;
    }

    public void RecordHint()
    {
        Stats.RecordHint();
    }

    public IReadOnlyList<LessonCharacterSnapshot> GetCurrentLineSnapshot()
    {
        string line = CurrentLine;
        var result = new LessonCharacterSnapshot[line.Length];
        for (int i = 0; i < line.Length; i++)
        {
            LessonCharacterState state;
            if (Mode == LessonTypingMode.Strict)
            {
                if (i < CursorPosition) state = LessonCharacterState.Correct;
                else if (i == CursorPosition) state = _strictCurrentWrong ? LessonCharacterState.Wrong : LessonCharacterState.Current;
                else state = LessonCharacterState.Pending;
            }
            else
            {
                if (i < _typedLine.Length)
                    state = _flexibleCorrectStates.Count > i && _flexibleCorrectStates[i] ? LessonCharacterState.Correct : LessonCharacterState.Wrong;
                else if (i == _typedLine.Length)
                    state = LessonCharacterState.Current;
                else
                    state = LessonCharacterState.Pending;
            }

            result[i] = new LessonCharacterSnapshot(line[i], state);
        }

        return result;
    }

    public IReadOnlyList<LessonTypedCharacterSnapshot> GetTypedLineSnapshot()
    {
        if (Mode == LessonTypingMode.Strict)
        {
            string line = CurrentLine;
            int correctLength = Math.Min(CursorPosition, line.Length);
            var result = new List<LessonTypedCharacterSnapshot>(correctLength + (_strictWrongCharacter.HasValue ? 1 : 0));
            for (int i = 0; i < correctLength; i++)
                result.Add(new LessonTypedCharacterSnapshot(line[i], LessonCharacterState.Correct));
            if (_strictWrongCharacter.HasValue)
                result.Add(new LessonTypedCharacterSnapshot(_strictWrongCharacter.Value, LessonCharacterState.Wrong));
            return result;
        }

        var typed = new LessonTypedCharacterSnapshot[_typedLine.Length];
        for (int i = 0; i < _typedLine.Length; i++)
        {
            var state = _flexibleCorrectStates.Count > i && _flexibleCorrectStates[i]
                ? LessonCharacterState.Correct
                : LessonCharacterState.Wrong;
            typed[i] = new LessonTypedCharacterSnapshot(_typedLine[i], state);
        }
        return typed;
    }

    private LessonInputResult TypeStrict(char c)
    {
        string line = CurrentLine;
        if (CursorPosition >= line.Length) return IgnoredResult();
        char expected = line[CursorPosition];
        bool ok = c == expected;
        Stats.RecordChar(c, expected);

        if (ok)
        {
            CursorPosition++;
            _strictCurrentWrong = false;
            _strictWrongCharacter = null;
            if (CursorPosition >= line.Length)
                return MarkLineComplete(expected, c, wasError: false);
            return new LessonInputResult(true, false, expected, c, false, false);
        }

        _strictCurrentWrong = true;
        _strictWrongCharacter = c;
        return new LessonInputResult(true, true, expected, c, false, false);
    }

    private LessonInputResult TypeFlexible(char c)
    {
        string line = CurrentLine;
        int pos = _typedLine.Length;
        char? expected = pos < line.Length ? line[pos] : null;
        bool ok = expected.HasValue && c == expected.Value;
        Stats.RecordChar(c, expected);
        _typedLine += c;
        _flexibleCorrectStates.Add(ok);
        CursorPosition = _typedLine.Length;

        if (_typedLine == line)
            return MarkLineComplete(expected, c, wasError: !ok);

        return new LessonInputResult(true, !ok, expected, c, false, false);
    }

    private LessonInputResult MarkLineComplete(char? expected, char actual, bool wasError)
    {
        IsLineComplete = true;
        bool exerciseCompleted = LineIndex >= _lines.Length - 1;
        if (exerciseCompleted)
            CompleteExercise();
        return new LessonInputResult(true, wasError, expected, actual, true, exerciseCompleted);
    }

    private void CompleteExercise()
    {
        IsExerciseComplete = true;
        IsLineComplete = true;
        Stats.Complete();
    }

    private void ResetLineState()
    {
        _typedLine = "";
        _strictCurrentWrong = false;
        _strictWrongCharacter = null;
        _flexibleCorrectStates.Clear();
    }

    private void RecomputeFlexibleStates()
    {
        _flexibleCorrectStates.Clear();
        string line = CurrentLine;
        for (int i = 0; i < _typedLine.Length; i++)
            _flexibleCorrectStates.Add(i < line.Length && _typedLine[i] == line[i]);
    }

    private static LessonInputResult IgnoredResult() => new(false, false, null, null, false, false);
}
