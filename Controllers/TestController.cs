using Microsoft.AspNetCore.Mvc;

[ApiController] // Indicates that this is an API controller (enables certain behaviors)
[Route("api/[controller]")] // Base route for all actions in this controller (e.g., /api/books)
public class BooksController : ControllerBase
{
    // Accessible via GET /api/books
    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(new { Message = "All Books" });
    }

    // Accessible via GET /api/books/{id} (where {id} is a route parameter)
    [HttpGet("{id:int}")] // Constraints can be added to route parameters (e.g., :int for integer)
    public IActionResult GetById(int id)
    {
        return Ok(new { Id = id, Title = "Some Book" });
    }

    // Accessible via POST /api/books
    [HttpPost]
    public IActionResult Create([FromBody] BookModel model) // [FromBody] indicates data comes from the request body
    {
        // ... logic to create a new book ...
        return CreatedAtAction(nameof(GetById), new { id = 1 }, model); // Returns 201 Created with location header
    }

    // Accessible via PUT /api/books/{id}
    [HttpPut("{id:int}")]
    public IActionResult Update(int id, [FromBody] BookModel model)
    {
        // ... logic to update the book with the given ID ...
        return NoContent(); // Returns 204 No Content on successful update
    }

    // Accessible via DELETE /api/books/{id}
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        // ... logic to delete the book with the given ID ...
        return NoContent(); // Returns 204 No Content on successful deletion
    }
}

public record struct BookModel(string Title, string Author) {}
