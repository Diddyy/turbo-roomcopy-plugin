using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Turbo.Database.Entities;

namespace TurboRoomCopy.Database;

/// <summary>
/// One room copy put to the panel: its dry run until a staff member imports it, then the room it
/// made and how each wired box saved. The copy itself is kept, so the import runs on what was
/// previewed.
/// </summary>
[Table("imports")]
[Index(nameof(Status))]
public class RoomImportEntity : TurboEntity
{
    public const string PREVIEW = "preview";
    public const string RUNNING = "running";
    public const string IMPORTED = "imported";
    public const string FAILED = "failed";

    /// <summary>The staff member who previewed it.</summary>
    [Column("staff_id")]
    public required int StaffId { get; set; }

    [Column("owner_id")]
    public required int OwnerId { get; set; }

    [Column("owner_name")]
    [MaxLength(64)]
    public required string OwnerName { get; set; }

    [Column("name_override")]
    [MaxLength(128)]
    public string? NameOverride { get; set; }

    /// <summary>The room's name as it will be made.</summary>
    [Column("room_name")]
    [MaxLength(128)]
    public required string RoomName { get; set; }

    [Column("source_hotel")]
    [MaxLength(64)]
    public string? SourceHotel { get; set; }

    [Column("source_room_id")]
    [MaxLength(32)]
    public string? SourceRoomId { get; set; }

    [Column("status")]
    [MaxLength(16)]
    public required string Status { get; set; }

    /// <summary>The room copy as it was pasted (JSON).</summary>
    [Column("document", TypeName = "longtext")]
    public required string Document { get; set; }

    /// <summary>The dry run (JSON): counts, furni left out, warnings.</summary>
    [Column("preview", TypeName = "longtext")]
    public required string Preview { get; set; }

    /// <summary>What the import did (JSON): the room, the furni made, each wired box saved or refused.</summary>
    [Column("report", TypeName = "longtext")]
    public string? Report { get; set; }

    [Column("room_id")]
    public int? RoomId { get; set; }

    [Column("imported_by")]
    public int? ImportedBy { get; set; }

    [Column("imported_at")]
    public DateTime? ImportedAt { get; set; }

    [Column("error")]
    [MaxLength(1000)]
    public string? Error { get; set; }
}
