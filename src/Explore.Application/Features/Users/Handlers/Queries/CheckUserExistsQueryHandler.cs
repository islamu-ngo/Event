using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Users.Handlers.Queries;

public class CheckUserExistsQueryHandler : IRequestHandler<CheckUserExistsQuery, bool>
{
    private readonly IUserRepository _userRepository;

    public CheckUserExistsQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<bool> Handle(CheckUserExistsQuery request, CancellationToken cancellationToken)
    {
        return await _userRepository.ExistsByEmail(request.Email);
    }
}
