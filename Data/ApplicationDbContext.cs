
using Microsoft.EntityFrameworkCore;
using WebCodeWork.Models;

namespace WebCodeWork.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Classroom> Classrooms { get; set; } 
        public DbSet<ClassroomMember> ClassroomMembers { get; set; } 

        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<AssignmentSubmission> AssignmentSubmissions { get; set; }
        public DbSet<SubmittedFile> SubmittedFiles { get; set; }

        public DbSet<TestCase> TestCases { get; set; } 

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<ClassroomMember>()
                .HasKey(cm => new { cm.UserId, cm.ClassroomId });

            modelBuilder.Entity<ClassroomMember>()
                .HasOne(cm => cm.User)
                .WithMany(u => u.ClassroomMemberships) 
                .HasForeignKey(cm => cm.UserId)
                .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<ClassroomMember>()
                .HasOne(cm => cm.Classroom)
                .WithMany(c => c.Members) 
                .HasForeignKey(cm => cm.ClassroomId)
                .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<ClassroomMember>()
                .Property(cm => cm.Role)
                .HasConversion<string>();

            modelBuilder.Entity<AssignmentSubmission>()
                .HasIndex(s => new { s.StudentId, s.AssignmentId })
                .IsUnique();

            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Classroom)
                .WithMany() 
                .HasForeignKey(a => a.ClassroomId)
                .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.CreatedBy)
                .WithMany() 
                .HasForeignKey(a => a.CreatedById)
                .OnDelete(DeleteBehavior.Restrict); 

            modelBuilder.Entity<AssignmentSubmission>()
               .HasOne(s => s.Assignment)
               .WithMany(a => a.Submissions)
               .HasForeignKey(s => s.AssignmentId)
               .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<AssignmentSubmission>()
                .HasOne(s => s.Student)
                .WithMany() 
                .HasForeignKey(s => s.StudentId)
                .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<AssignmentSubmission>()
               .HasOne(s => s.GradedBy)
               .WithMany()
               .HasForeignKey(s => s.GradedById)
               .OnDelete(DeleteBehavior.SetNull); 

            modelBuilder.Entity<SubmittedFile>()
                .HasOne(f => f.AssignmentSubmission)
                .WithMany(s => s.SubmittedFiles)
                .HasForeignKey(f => f.AssignmentSubmissionId)
                .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<TestCase>()
                .HasOne(tc => tc.Assignment)
                .WithMany(a => a.TestCases) 
                .HasForeignKey(tc => tc.AssignmentId)
                .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<TestCase>()
                .HasOne(tc => tc.AddedBy)
                .WithMany() 
                .HasForeignKey(tc => tc.AddedById)
                .OnDelete(DeleteBehavior.Restrict); 
        }
    }
}