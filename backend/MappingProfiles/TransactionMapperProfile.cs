using AutoMapper;
using WalletBackend.Models;
using WalletBackend.Models.DTOS;
using WalletBackend.Models.DTOS.Transaction;
using WalletBackend.Models;


namespace WalletBackend.MappingProfiles;

public class TransactionMapperProfile : Profile
{
    public TransactionMapperProfile()
    {

        CreateMap<CreateTransactionModel, Transaction>();


        CreateMap<DeleteTransactionModel, Transaction>();

        CreateMap<UpdateTransactionModel, Transaction>();
        CreateMap<ViewTransactionModel, Transaction>();



    }
}