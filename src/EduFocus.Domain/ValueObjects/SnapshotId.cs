using System;

namespace EduFocus.Domain.ValueObjects;

public readonly record struct SnapshotId(Guid Value)
{
    public static SnapshotId New() => new(Guid.NewGuid());
    public static readonly SnapshotId Empty = new(Guid.Empty);
}
