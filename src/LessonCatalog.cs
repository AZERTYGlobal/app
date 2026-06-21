using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AZERTYGlobal;

internal enum LessonTypingMode
{
    Flexible,
    Strict
}

internal sealed class LessonCatalog
{
    public LessonCatalog(IReadOnlyList<LessonModule> modules)
    {
        Modules = modules;
        Exercises = modules
            .SelectMany(module => module.Lessons)
            .SelectMany(lesson => lesson.Exercises)
            .ToArray();
    }

    public IReadOnlyList<LessonModule> Modules { get; }
    public IReadOnlyList<LessonExercise> Exercises { get; }
    public int TotalExerciseCount => Exercises.Count;
    public int SiteModuleCount => Modules.Count(module => !module.IsSynthetic);
    public int SiteLessonCount => Modules.Where(module => !module.IsSynthetic).SelectMany(module => module.Lessons).Count();
    public int SiteExerciseCount => Modules.Where(module => !module.IsSynthetic).SelectMany(module => module.Lessons).SelectMany(lesson => lesson.Exercises).Count();

    public LessonExercise? FindExercise(string moduleId, string lessonId, int exerciseIndex)
    {
        foreach (var module in Modules)
        {
            if (!StringComparer.Ordinal.Equals(module.Id, moduleId)) continue;
            foreach (var lesson in module.Lessons)
            {
                if (!StringComparer.Ordinal.Equals(lesson.Id, lessonId)) continue;
                return lesson.Exercises.FirstOrDefault(exercise => exercise.ExerciseIndex == exerciseIndex);
            }
        }

        return null;
    }
}

internal sealed class LessonModule
{
    public LessonModule(string id, string title, string description, string icon, bool isSynthetic, IReadOnlyList<LessonLesson> lessons)
    {
        Id = id;
        Title = title;
        Description = description;
        Icon = icon;
        IsSynthetic = isSynthetic;
        Lessons = lessons;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Icon { get; }
    public bool IsSynthetic { get; }
    public IReadOnlyList<LessonLesson> Lessons { get; }
}

internal sealed class LessonLesson
{
    public LessonLesson(string moduleId, string id, string title, string description, IReadOnlyList<string> characters, IReadOnlyList<LessonExercise> exercises)
    {
        ModuleId = moduleId;
        Id = id;
        Title = title;
        Description = description;
        Characters = characters;
        Exercises = exercises;
    }

    public string ModuleId { get; }
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<string> Characters { get; }
    public IReadOnlyList<LessonExercise> Exercises { get; }
}

internal sealed class LessonExercise
{
    public LessonExercise(
        string moduleId,
        string lessonId,
        int exerciseIndex,
        string type,
        string instruction,
        string content,
        LessonTypingMode typingMode)
    {
        ModuleId = moduleId;
        LessonId = lessonId;
        ExerciseIndex = exerciseIndex;
        Type = type;
        Instruction = instruction;
        Content = content.Replace("\r\n", "\n");
        TypingMode = typingMode;
        Hash = LessonCatalogLoader.ComputeExerciseHash(type, instruction, Content);
    }

    public string ModuleId { get; }
    public string LessonId { get; }
    public int ExerciseIndex { get; }
    public string Type { get; }
    public string Instruction { get; }
    public string Content { get; }
    public LessonTypingMode TypingMode { get; }
    public string Hash { get; }
    public string StableKey => $"{ModuleId}/{LessonId}/{ExerciseIndex}/{Hash}";
    public string[] Lines => Content.Split('\n');
}

internal static class LessonCatalogLoader
{
    public const string LessonsResourceName = "lessons.json";
    public const string InitiationModuleId = "initiation";
    public const string InitiationLessonId = "premiers-pas";

    public static LessonCatalog LoadFromResource(string resourceName = LessonsResourceName)
    {
        using var stream = typeof(LessonCatalogLoader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Ressource '{resourceName}' introuvable dans l'assemblage.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    public static LessonCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var modules = new List<LessonModule> { BuildInitiationModule() };

        foreach (var moduleEl in doc.RootElement.GetProperty("modules").EnumerateArray())
        {
            string moduleId = RequiredString(moduleEl, "id");
            var lessons = new List<LessonLesson>();

            foreach (var lessonEl in moduleEl.GetProperty("lessons").EnumerateArray())
            {
                string lessonId = RequiredString(lessonEl, "id");
                var characters = new List<string>();
                if (lessonEl.TryGetProperty("characters", out var charsEl) && charsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ch in charsEl.EnumerateArray())
                        characters.Add(ch.GetString() ?? "");
                }

                var exercises = new List<LessonExercise>();
                int exerciseIndex = 0;
                foreach (var exerciseEl in lessonEl.GetProperty("exercises").EnumerateArray())
                {
                    string type = RequiredString(exerciseEl, "type");
                    if (!StringComparer.Ordinal.Equals(type, "practice"))
                        throw new NotSupportedException($"Type d'exercice non supporté en v1: {type}");

                    exercises.Add(new LessonExercise(
                        moduleId,
                        lessonId,
                        exerciseIndex,
                        type,
                        RequiredString(exerciseEl, "instruction"),
                        RequiredString(exerciseEl, "content"),
                        LessonTypingMode.Flexible));
                    exerciseIndex++;
                }

                lessons.Add(new LessonLesson(
                    moduleId,
                    lessonId,
                    RequiredString(lessonEl, "title"),
                    OptionalString(lessonEl, "description"),
                    characters,
                    exercises));
            }

            modules.Add(new LessonModule(
                moduleId,
                RequiredString(moduleEl, "title"),
                OptionalString(moduleEl, "description"),
                OptionalString(moduleEl, "icon"),
                isSynthetic: false,
                lessons));
        }

        return new LessonCatalog(modules);
    }

    internal static string ComputeExerciseHash(string type, string instruction, string content)
    {
        string normalized = string.Join('\u001F', type, instruction, content.Replace("\r\n", "\n"));
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static LessonModule BuildInitiationModule()
    {
        var exercises = new List<LessonExercise>
        {
            InitiationExercise(0, "Tape É pour découvrir les majuscules accentuées.", "É"),
            InitiationExercise(1, "Tape cette phrase en utilisant Verr. Maj pour les capitales accentuées.", "GRÂCE À AZERTY GLOBAL, ÉCRIRE EN FRANÇAIS EST TRÈS FACILE !"),
            InitiationExercise(2, "Tape cette adresse e-mail.", "jean.dupont@education.gouv.fr"),
            InitiationExercise(3, "Tape cette phrase de typographie française.", "Lætitia demande « d'où vient ce chef-d'œuvre… » — elle l'approuve à 100 %."),
            InitiationExercise(4, "Tape cette ligne de code.", "type Config = { items: string[]; sep: \"~\" | \"\\\\\" };"),
            InitiationExercise(5, "Tape ces mots étrangers.", "São Paulo, Córdoba, Tromsø, Łódź, lunedì, Größe")
        };

        var lesson = new LessonLesson(
            InitiationModuleId,
            InitiationLessonId,
            "Premiers pas",
            "Les 6 exercices courts de l'initiation AZERTY Global.",
            Array.Empty<string>(),
            exercises);

        return new LessonModule(
            InitiationModuleId,
            "Initiation",
            "Rejouer le parcours de prise en main intégré à l'accueil.",
            "🚀",
            isSynthetic: true,
            new[] { lesson });
    }

    private static LessonExercise InitiationExercise(int index, string instruction, string content)
    {
        return new LessonExercise(
            InitiationModuleId,
            InitiationLessonId,
            index,
            "practice",
            instruction,
            content,
            LessonTypingMode.Strict);
    }

    private static string RequiredString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? "";
        throw new JsonException($"Propriété string requise manquante: {property}");
    }

    private static string OptionalString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }
}
