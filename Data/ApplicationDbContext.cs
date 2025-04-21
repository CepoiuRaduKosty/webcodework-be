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

        }
    }
}