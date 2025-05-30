import { getBalance, sendMoney , getBalanceWallet, getTransactions, unlockWallet, fetchWalletStatus} from "@/api/routes/transactions"
import { useQuery, useMutation } from "@tanstack/react-query"
import { useQueryClient } from "@tanstack/react-query"

interface SendMoneyParams {
  senderAddress: string;
  amount: number;
  walletId: string;
  receiverAddress: string;
  description: string;
  currency: number;
}

export const useBalance = (walletAddress: string) => {
  return useQuery({
    queryKey: ['transactions', walletAddress],
    queryFn: () => getBalance(walletAddress),
    enabled: !!walletAddress,
    staleTime: 1000 * 60 * 5, // 5 minutes
    retry: 2,
  });
};



export const useTransactions = (walletId: string) => {
  return useQuery({
    queryKey: ['transactions', walletId], // Fixed: changed from 'balance' to 'transactions'
    queryFn: () => getTransactions(walletId),
    enabled: !!walletId,
    staleTime: 1000 * 60 * 5, // 5 minutes
    retry: 2,
  });
}

export const useBalanceWallet = (userId: string) => {
  return useQuery({
    queryKey: ['balanceWallet', userId],
    queryFn: () => getBalanceWallet(userId),
    enabled: !!userId,
    staleTime: 1000 * 60 * 5, // 5 minutes
    retry: 2,
  })
}

export const useWalletStatus = (walletId: string) => {
  return useQuery({
    queryKey: ['status', walletId], // Fixed: changed from 'balance' to 'transactions'
    queryFn: () => fetchWalletStatus(walletId),
    enabled: !!walletId,
    staleTime: 1000 * 60 * 5, // 5 minutes
    retry: 2,
  });
}

export const useUnlockWallet = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ walletId, password }: { walletId: string; password: string }) =>
      unlockWallet(walletId, password),
    onSuccess: (data, variables) => {
      // Invalidate and refetch relevant queries
      queryClient.invalidateQueries({ queryKey: ['transactions', variables.walletId] });
    },
  });
}

export const useSendMoney = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ senderAddress, amount, walletId, receiverAddress, description, currency }: SendMoneyParams) =>
      sendMoney(senderAddress, amount, walletId, receiverAddress, description, currency),
    onSuccess: (data, variables) => {
      // Invalidate and refetch relevant queries
      queryClient.invalidateQueries({ queryKey: ['balance', variables.senderAddress] });
      queryClient.invalidateQueries({ queryKey: ['balance', variables.receiverAddress] });
      queryClient.invalidateQueries({ queryKey: ['transactions', variables.walletId] });
      queryClient.invalidateQueries({ queryKey: ['balanceWallet'] });
    },
  });
}