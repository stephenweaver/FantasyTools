using FantasyTools.Api.Documents;
using FantasyTools.Api.Models;

namespace FantasyTools.Api.Services;

public interface ICardWorkspaceService
{
    Task<CardWorkspaceDocument> Get(string leagueId, string userId);
    Task<CardDraftDocument> Create(string leagueId, string userId, string userName, SaveCardDraftRequest request);
    Task<CardDraftDocument> Update(string leagueId, string cardId, string userId, string userName, SaveCardDraftRequest request);
    Task<CardDraftDocument> ChangeStatus(string leagueId, string cardId, string userId, string userName, string status);
    Task SetCollaborator(string leagueId, string actorUserId, ChangeCardCollaboratorRequest request);
}
