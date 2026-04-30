// Attributs assembly. GenerateAssemblyInfo est désactivé dans le csproj
// (cf. WACK warning sur le commentaire MSBuild WriteCodeFragment).
// Donc on déclare manuellement ce qu'il faut.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AZERTYGlobal.Tests")]
