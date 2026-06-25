using PaymentServices.RTPSend.Models;

namespace PaymentServices.RTPSend.Interface.Adapters;

public interface IServiceBusAdapter
{
    Task SendMessage(ServiceBusRequest serviceBusRequest);
}
