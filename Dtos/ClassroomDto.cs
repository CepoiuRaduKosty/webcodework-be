namespace WebCodeWork.Dtos
{
    public class ClassroomDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? PhotoUrl { get; set; }
    }

    public class LeaveClassroomRequestDto
    {
        /// <summary>
        /// Required only if the current user is the owner of the classroom
        /// and there are other teachers to promote.
        /// This must be the UserId of an existing teacher in the classroom.
        /// </summary>
        public int? NewOwnerUserId { get; set; }
    }
}