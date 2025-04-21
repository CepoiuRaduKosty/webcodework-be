namespace WebCodeWork.Dtos
{
    public class ClassroomDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        // You might add owner info or member counts here
    }
}