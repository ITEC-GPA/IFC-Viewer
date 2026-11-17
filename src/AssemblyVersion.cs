// The version of Meerkat, the only place where it is written: Meerkat.csproj compiles it (Build.ps1 runs dotnet build), and
// MeerkatInfo shows it in Grasshopper. At every release: a fix raises the last number, anything else the second one.
using System.Reflection;

[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]
[assembly: AssemblyInformationalVersion("0.2.0")]
