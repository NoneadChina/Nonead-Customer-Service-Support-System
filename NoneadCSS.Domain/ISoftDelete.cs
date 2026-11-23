namespace NoneadCSS.Domain;

public interface ISoftDelete
{
    DateTime? DeletedAt { get; set; }
}
