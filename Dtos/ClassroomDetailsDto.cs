
using WebCodeWork.Enums;
using System.Collections.Generic; 

namespace WebCodeWork.Dtos
{
    
    
    
    
    public class ClassroomDetailsDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ClassroomRole CurrentUserRole { get; set; } 
        public List<ClassroomMemberDto> Members { get; set; } = new List<ClassroomMemberDto>(); 
        public string? PhotoUrl { get; set; }
    }
}