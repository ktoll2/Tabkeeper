using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BranchPins.Core;

/// <summary>
/// Portable, validated representation of a saved pin set.
/// </summary>
[DataContract]
public sealed class PinSetTransfer
{
    [DataMember(Name = "formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [DataMember(Name = "sourceBranch")]
    public string SourceBranch { get; set; } = string.Empty;

    [DataMember(Name = "pinnedPaths")]
    public List<string> PinnedPaths { get; set; } = new List<string>();

    /// <summary>
    /// Exports validated repository-relative paths to a portable pin-set JSON document.
    /// </summary>
    /// <param name="filePath">The destination JSON file path.</param>
    /// <param name="sourceBranch">The source branch recorded as export metadata.</param>
    /// <param name="pinnedPaths">Candidate paths to validate, deduplicate, and export.</param>
    public static void Export(string filePath, string sourceBranch, IEnumerable<string> pinnedPaths)
    {
        PinSetTransfer transfer = new PinSetTransfer
        {
            SourceBranch = sourceBranch,
            PinnedPaths = NormalizePaths(pinnedPaths).ToList(),
        };
        using FileStream stream = File.Create(filePath);
        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PinSetTransfer));
        serializer.WriteObject(stream, transfer);
    }

    /// <summary>
    /// Imports a supported pin-set document and removes unsafe or duplicate paths.
    /// </summary>
    /// <param name="filePath">The portable pin-set JSON file to read.</param>
    /// <returns>A validated transfer document.</returns>
    /// <exception cref="SerializationException">Thrown when the file has an unsupported or invalid format.</exception>
    public static PinSetTransfer Import(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PinSetTransfer));
        PinSetTransfer transfer = serializer.ReadObject(stream) as PinSetTransfer
            ?? throw new SerializationException("The pin-set file is invalid.");
        if (transfer.FormatVersion != 1)
        {
            throw new SerializationException("The pin-set file uses an unsupported format.");
        }

        transfer.PinnedPaths = NormalizePaths(transfer.PinnedPaths).ToList();
        return transfer;
    }

    /// <summary>
    /// Removes unsafe, blank, and duplicate paths before producing a stable portable path sequence.
    /// </summary>
    /// <param name="paths">Candidate relative paths.</param>
    /// <returns>Sorted, distinct, safe repository-relative paths.</returns>
    private static IEnumerable<string> NormalizePaths(IEnumerable<string> paths)
    {
        return paths.Where(path => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
                !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment == ".."))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }
}
