using Akka;
using Akka.Streams.Dsl;

namespace Servus.Akka.IO;

public interface ITransportFactory
{
    Flow<IOutputItem, IInputItem, NotUsed> Create();
}