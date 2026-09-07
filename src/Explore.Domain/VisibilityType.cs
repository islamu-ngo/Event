// Defines visibility levels like Public, Private, Members-only, etc.

using System;
using System.ComponentModel.DataAnnotations;

namespace Explore.Domain;

public class VisibilityType
{
    public int Id { get; set; }

    public required string MasterCode { get; set; }

    public required string FullName { get; set; }

    public string? Description { get; set; }
}
