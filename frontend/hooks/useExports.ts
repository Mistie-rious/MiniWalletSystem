import { exportToCSV, exportToPDF } from "@/api/routes/exports";
import { useQuery } from "@tanstack/react-query";

export const useExportToCSV = (walletId: string) => {
    return useQuery({
      queryKey: ['CSV', walletId],
      queryFn: () => exportToCSV(walletId),
      enabled: !!walletId,
      staleTime: 1000 * 60 * 5, // 5 minutes
      retry: 2,
    });
  };


  export const useExportToPDF = (walletId: string, startDate?: string, endDate?: string) => {
    return useQuery({
      queryKey: ['PDF', walletId, startDate, endDate],
      queryFn: () => exportToPDF(walletId, startDate, endDate), // ← was exportToCSV
      enabled: !!walletId,
      staleTime: 1000 * 60 * 5, // 5 minutes
      retry: 2,
    });
  };