namespace FamilyLife.Domain
{
    public sealed partial class Lineage
    {
        public int CrestPrimary = -1, CrestSecondary = 7, CrestMark = -1, CrestPattern;
    }
    public static class CrestStyle
    {
        public const int Colors = 8, Marks = 6, Patterns = 3;
        public static int Index(int value,int fallback,int count)
        {int chosen=value<0?fallback:value;return (chosen%count+count)%count;}
    }
}
