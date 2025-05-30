import apiClient from "../apiClient";

export const getBalance = async (walletAddress: string) => {
    const {data} = await apiClient.get('transactions/balance', {
        params: {address: walletAddress}
    }
    )
    return data;
}

export const sendMoney = async (senderAddress: string, amount: number, walletId: string, receiverAddress: string, description: string, currency: number) => {
    // Debug logging
 

    // Validate parameters before sending
    if (!senderAddress) {
        throw new Error('senderAddress is required');
    }
    if (!walletId) {
        throw new Error('walletId is required');
    }
    if (!receiverAddress) {
        throw new Error('receiverAddress is required');
    }
    if (!amount || amount <= 0) {
        throw new Error('amount must be greater than 0');
    }

    const payload = {
        senderAddress: senderAddress,
        amount,
        walletId,
        receiverAddress,
        description,
        currency
    };



    try {
        const { data } = await apiClient.post('transactions/send', payload);
        console.log('API response:', data);
        return data;
    } catch (error) {
        console.error('API Error:', error);
        if (error) {
            console.error('Error response data:', error)
        }
        throw error;
    }
}

export const unlockWallet = async (walletId: string, password: string) => {
    const {data} = await apiClient.post('transactions/unlock', {
        walletId, password
    })
    return data;
}

export const getBalanceWallet = async (userId: string) => {
    const {data} = await apiClient.get('transactions/balance', {
        params: {userId: userId}
    })
    return data;
}

export const getTransactions = async (walletId: string) => {
    const {data} = await apiClient.get('transactions/search', {
        params: {walletId: walletId}
    })
    return data;

}

export const fetchWalletStatus = async (walletId: string) => {
    const {data} = await apiClient.get('transactions/status}', {
        params: {walletId: walletId}
    })

}