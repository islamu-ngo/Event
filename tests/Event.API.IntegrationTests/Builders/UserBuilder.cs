using Explore.Domain;

namespace Event.Api.IntegrationTests.Builders;

/// <summary>
/// Builds <see cref="User"/> instances with embedded <see cref="UserPii"/> for test data seeding.
/// Defaults to a unique email address to avoid constraint violations across tests.
/// </summary>
public sealed class UserBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _email = $"test-{Guid.NewGuid().ToString("N")[..8]}@example.com";
    private string _firstName = "Test";
    private string _lastName = "User";

    public UserBuilder WithId(Guid id) { _id = id; return this; }
    public UserBuilder WithEmail(string email) { _email = email; return this; }
    public UserBuilder WithFirstName(string firstName) { _firstName = firstName; return this; }
    public UserBuilder WithLastName(string lastName) { _lastName = lastName; return this; }

    public User Build() => new()
    {
        Id = _id,
        Pii = new UserPii
        {
            Email = _email,
            FirstName = _firstName,
            LastName = _lastName
        }
    };
}
