// Enums/ClassroomRole.cs
namespace WebCodeWork.Enums
{
    public enum ClassroomRole
    {
        Owner,    // The creator/administrator
        Teacher,  // Can manage students, content (permissions TBD later)
        Student   // Member with basic access
    }
}