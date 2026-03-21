namespace HoldFast.Domain.Entities;

/// <summary>
/// Base class for all domain entities. Provides auto-incrementing integer Id and
/// GORM-style timestamp columns (CreatedAt, UpdatedAt, soft-delete via DeletedAt).
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Base class for entities requiring a 64-bit primary key (e.g., high-volume tables).
/// Same timestamp semantics as <see cref="BaseEntity"/>.
/// </summary>
public abstract class BaseInt64Entity
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
