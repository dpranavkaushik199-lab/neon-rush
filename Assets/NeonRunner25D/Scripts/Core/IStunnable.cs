namespace NeonRunner
{
    /// <summary>Shared contract for anything the arm can disable (Taser, Grapple-hack).</summary>
    public interface IStunnable
    {
        bool IsStunned { get; }
        void Stun(float duration);
    }
}
