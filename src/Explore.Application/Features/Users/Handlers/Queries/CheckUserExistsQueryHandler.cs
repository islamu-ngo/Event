using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;

namespace Explore.Application.Features.Users.Handlers.Queries;

public class CheckUserExistsQueryHandler : IQueryHandler<CheckUserExistsQuery, bool>
{
    private readonly IUserRepository _userRepository;

    public CheckUserExistsQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<bool> QueryAsync(CheckUserExistsQuery request, CancellationToken cancellationToken = default)
    {
        return await _userRepository.ExistsByEmail(request.Email);
    }
}
