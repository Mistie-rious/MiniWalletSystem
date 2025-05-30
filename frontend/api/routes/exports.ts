// api/exports.ts
import apiClient from "../apiClient";

export const exportToCSV = async (walletId: string, startDate?: string, endDate?: string) => {
    const { data } = await apiClient.get(`/Export/csv/${walletId}`, {
        params: {  startDate, endDate }
    });
    return data;
};

export const exportToPDF = async (
    walletId: string,
    startDate?: string,
    endDate?: string
  ): Promise<Blob> => {
    const response = await apiClient.get(`/Export/pdf/${walletId}`, {
      params: { startDate, endDate },
      responseType: 'blob',                 // ← tell axios you want a Blob
      headers: { Accept: 'application/pdf' } // ← optional but good practice
    });
    return response.data;                   // this is now a Blob
  };
  