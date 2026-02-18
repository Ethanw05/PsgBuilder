namespace PsgBuilder.Core.Psg;

/// <summary>
/// One subreference record (8 bytes): objectId (0-based dict index), offset within that object.
/// </summary>
public readonly record struct PsgSubrefRecord(uint ObjectDictIndex, uint OffsetInObject);
