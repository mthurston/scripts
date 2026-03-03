using System.Data;

namespace KnowledgeBase.Core.Interfaces;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
