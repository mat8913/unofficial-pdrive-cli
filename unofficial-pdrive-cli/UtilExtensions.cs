namespace unofficial_pdrive_cli;

public static class UtilExtensions
{
    public static bool StartsWith<T>(this IReadOnlyList<T> lhs, IReadOnlyList<T> rhs)
        where T : IEquatable<T>
    {
        if (lhs.Count < rhs.Count)
            return false;

        for (int i = 0; i < rhs.Count; ++i)
        {
            if (!lhs[i].Equals(rhs[i]))
            {
                return false;
            }
        }

        return true;
    }
}
