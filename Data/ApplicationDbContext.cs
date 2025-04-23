// Data/ApplicationDbContext.cs
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
        public DbSet<Classroom> Classrooms { get; set; } // Add DbSet for Classroom
        public DbSet<ClassroomMember> ClassroomMembers { get; set; } // Add DbSet for the join entity

        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<AssignmentSubmission> AssignmentSubmissions { get; set; }
        public DbSet<SubmittedFile> SubmittedFiles { get; set; }



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            // --- ClassroomMember Configuration ---

            // Define composite primary key
            modelBuilder.Entity<ClassroomMember>()
                .HasKey(cm => new { cm.UserId, cm.ClassroomId });

            // Configure the relationship from User to ClassroomMember (One-to-Many)
            modelBuilder.Entity<ClassroomMember>()
                .HasOne(cm => cm.User)
                .WithMany(u => u.ClassroomMemberships) // Use the navigation property in User
                .HasForeignKey(cm => cm.UserId)
                .OnDelete(DeleteBehavior.Cascade); // Or Restrict, depending on desired behavior when a User is deleted

            // Configure the relationship from Classroom to ClassroomMember (One-to-Many)
            modelBuilder.Entity<ClassroomMember>()
                .HasOne(cm => cm.Classroom)
                .WithMany(c => c.Members) // Use the navigation property in Classroom
                .HasForeignKey(cm => cm.ClassroomId)
                .OnDelete(DeleteBehavior.Cascade); // If a Classroom is deleted, remove memberships

            // Store the Enum as a string in the database (recommended for readability)
            modelBuilder.Entity<ClassroomMember>()
                .Property(cm => cm.Role)
                .HasConversion<string>();


            // AssignmentSubmission unique constraint
            modelBuilder.Entity<AssignmentSubmission>()
                .HasIndex(s => new { s.StudentId, s.AssignmentId })
                .IsUnique();

            // Configure relationships and cascade deletes as needed
            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Classroom)
                .WithMany() // Assuming Classroom doesn't need direct nav prop back to all Assignments
                .HasForeignKey(a => a.ClassroomId)
                .OnDelete(DeleteBehavior.Cascade); // Deleting classroom deletes assignments

            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.CreatedBy)
                .WithMany() // Assuming User doesn't need direct nav prop back to created assignments
                .HasForeignKey(a => a.CreatedById)
                .OnDelete(DeleteBehavior.Restrict); // Prevent deleting user if they created assignments? Or SetNull?

            modelBuilder.Entity<AssignmentSubmission>()
               .HasOne(s => s.Assignment)
               .WithMany(a => a.Submissions)
               .HasForeignKey(s => s.AssignmentId)
               .OnDelete(DeleteBehavior.Cascade); // Deleting assignment deletes submissions

            modelBuilder.Entity<AssignmentSubmission>()
                .HasOne(s => s.Student)
                .WithMany() // Assuming User doesn't need direct nav prop to all submissions
                .HasForeignKey(s => s.StudentId)
                .OnDelete(DeleteBehavior.Cascade); // Deleting student deletes their submissions

            modelBuilder.Entity<AssignmentSubmission>()
               .HasOne(s => s.GradedBy)
               .WithMany()
               .HasForeignKey(s => s.GradedById)
               .OnDelete(DeleteBehavior.SetNull); // If grader is deleted, keep submission but nullify grader

            modelBuilder.Entity<SubmittedFile>()
                .HasOne(f => f.AssignmentSubmission)
                .WithMany(s => s.SubmittedFiles)
                .HasForeignKey(f => f.AssignmentSubmissionId)
                .OnDelete(DeleteBehavior.Cascade); // Deleting submission deletes its files


        }
    }
}