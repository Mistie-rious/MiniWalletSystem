"use client";

import { useTransactions } from "@/hooks/useTransactions";
import { DashboardLayout } from "@/components/dashboard-layout";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  ArrowUpRight,
  ArrowDownLeft,
  Download,
  Search,
  Filter,
  ChevronDown,
  Calendar,
} from "lucide-react";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuItem,
  DropdownMenuCheckboxItem,
} from "@/components/ui/dropdown-menu";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { useMe } from "@/hooks/useMe";
import { useState, useMemo } from "react";
import { useExportToCSV, useExportToPDF } from "@/hooks/useExports";
import LoadingSpinner from "@/components/loadingSpinner";

// Define types for better type safety
interface Transaction {
  id: string;
  type: number; // 0: credit, 1: debit, 2: internal
  amount: string | number;
  currency: number; // 0: ETH, 1: BTC, 2: USDT, 3: USDC
  status: number; // 0: pending, 1: successful, 2: failed
  createdAt: string;
  senderAddress: string;
  receiverAddress: string;
}

type TransactionType = 'all' | 'credit' | 'debit' | 'internal';
type DateRange = 'all' | '7days' | '30days' | '90days';
type Status = 'all' | 'pending' | 'successful' | 'failed';
type ExportDateRange = 'all' | 'today' | 'yesterday' | '7days' | '30days' | '90days' | 'thisMonth' | 'lastMonth' | 'thisYear' | 'lastYear' | 'custom';

// Helper functions - moved outside component to avoid hoisting issues
const getCurrencyLabel = (currency: number): string => {
  switch (currency) {
    case 0: return "ETH";
    case 1: return "BTC";
    case 2: return "USDT";
    case 3: return "USDC";
    default: return "Unknown";
  }
};

const getStatusLabel = (status: number): string => {
  switch (status) {
    case 0: return "Pending";
    case 1: return "Successful";
    case 2: return "Failed";
    default: return "Unknown";
  }
};

const getStatusStyles = (status: number): string => {
  switch (status) {
    case 0: return "bg-yellow-100 text-yellow-800";
    case 1: return "bg-green-100 text-green-700";
    case 2: return "bg-red-100 text-red-700";
    default: return "bg-gray-100 text-gray-700";
  }
};

// Helper function to get date range based on filter
const getDateRange = (dateFilter: DateRange): { startDate?: string; endDate?: string } => {
  if (dateFilter === 'all') return {};
  
  const now = new Date();
  const endDate = now.toISOString().split('T')[0]; // Today in YYYY-MM-DD format
  const startDate = new Date();
  
  switch (dateFilter) {
    case '7days':
      startDate.setDate(now.getDate() - 7);
      break;
    case '30days':
      startDate.setDate(now.getDate() - 30);
      break;
    case '90days':
      startDate.setDate(now.getDate() - 90);
      break;
    default:
      return {};
  }
  
  return {
    startDate: startDate.toISOString().split('T')[0],
    endDate
  };
};

// Enhanced function for export date ranges
const getExportDateRange = (dateFilter: ExportDateRange, customStart?: string, customEnd?: string): { startDate?: string; endDate?: string } => {
  if (dateFilter === 'all') return {};
  if (dateFilter === 'custom') {
    return {
      startDate: customStart,
      endDate: customEnd
    };
  }
  
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  
  switch (dateFilter) {
    case 'today':
      return {
        startDate: today.toISOString().split('T')[0],
        endDate: today.toISOString().split('T')[0]
      };
    
    case 'yesterday':
      const yesterday = new Date(today);
      yesterday.setDate(yesterday.getDate() - 1);
      return {
        startDate: yesterday.toISOString().split('T')[0],
        endDate: yesterday.toISOString().split('T')[0]
      };
    
    case '7days':
      const week = new Date(today);
      week.setDate(week.getDate() - 7);
      return {
        startDate: week.toISOString().split('T')[0],
        endDate: today.toISOString().split('T')[0]
      };
    
    case '30days':
      const month = new Date(today);
      month.setDate(month.getDate() - 30);
      return {
        startDate: month.toISOString().split('T')[0],
        endDate: today.toISOString().split('T')[0]
      };
    
    case '90days':
      const quarter = new Date(today);
      quarter.setDate(quarter.getDate() - 90);
      return {
        startDate: quarter.toISOString().split('T')[0],
        endDate: today.toISOString().split('T')[0]
      };
    
    case 'thisMonth':
      const thisMonthStart = new Date(now.getFullYear(), now.getMonth(), 1);
      return {
        startDate: thisMonthStart.toISOString().split('T')[0],
        endDate: today.toISOString().split('T')[0]
      };
    
    case 'lastMonth':
      const lastMonthStart = new Date(now.getFullYear(), now.getMonth() - 1, 1);
      const lastMonthEnd = new Date(now.getFullYear(), now.getMonth(), 0);
      return {
        startDate: lastMonthStart.toISOString().split('T')[0],
        endDate: lastMonthEnd.toISOString().split('T')[0]
      };
    
    case 'thisYear':
      const thisYearStart = new Date(now.getFullYear(), 0, 1);
      return {
        startDate: thisYearStart.toISOString().split('T')[0],
        endDate: today.toISOString().split('T')[0]
      };
    
    case 'lastYear':
      const lastYearStart = new Date(now.getFullYear() - 1, 0, 1);
      const lastYearEnd = new Date(now.getFullYear() - 1, 11, 31);
      return {
        startDate: lastYearStart.toISOString().split('T')[0],
        endDate: lastYearEnd.toISOString().split('T')[0]
      };
    
    default:
      return {};
  }
};

// Get readable label for export date range
const getExportDateRangeLabel = (dateFilter: ExportDateRange): string => {
  switch (dateFilter) {
    case 'all': return 'All Time';
    case 'today': return 'Today';
    case 'yesterday': return 'Yesterday';
    case '7days': return 'Last 7 Days';
    case '30days': return 'Last 30 Days';
    case '90days': return 'Last 90 Days';
    case 'thisMonth': return 'This Month';
    case 'lastMonth': return 'Last Month';
    case 'thisYear': return 'This Year';
    case 'lastYear': return 'Last Year';
    case 'custom': return 'Custom Range';
    default: return 'All Time';
  }
};

export default function Transactions() {
  const { data: me, isLoading: meLoading, error: meError } = useMe();
  
  const walletId = me?.data.walletId ?? "";
  const { data: transactions = [], isLoading } = useTransactions(walletId);

  const ethToNairaRate = 4500000; 

  // Filter and search states
  const [searchTerm, setSearchTerm] = useState("");
  const [transactionTypeFilter, setTransactionTypeFilter] = useState<TransactionType>('all');
  const [dateRangeFilter, setDateRangeFilter] = useState<DateRange>('all');
  const [statusFilter, setStatusFilter] = useState<Status>('all');

  // Export dialog states
  const [exportDialogOpen, setExportDialogOpen] = useState(false);
  const [exportFormat, setExportFormat] = useState<'csv' | 'pdf'>('csv');
  const [exportDateRange, setExportDateRange] = useState<ExportDateRange>('all');
  const [customStartDate, setCustomStartDate] = useState('');
  const [customEndDate, setCustomEndDate] = useState('');

  // Get date range for current filter (for display filtering)
  const { startDate, endDate } = getDateRange(dateRangeFilter);

  // Use export hooks
  const { 
    data: csvData, 
    isLoading: csvLoading, 
    error: csvError,
    refetch: refetchCSV
  } = useExportToCSV(walletId);
  
  const { 
    data: pdfData, 
    isLoading: pdfLoading, 
    error: pdfError,
    refetch: refetchPDF
  } = useExportToPDF(walletId);

  // Filtered and searched transactions
  const filteredTransactions = useMemo(() => {
    let filtered = transactions as Transaction[];

    // Search filter
    if (searchTerm) {
      const lowercaseSearch = searchTerm.toLowerCase();
      filtered = filtered.filter((tx) => {
        const senderMatch = tx.senderAddress?.toLowerCase().includes(lowercaseSearch);
        const receiverMatch = tx.receiverAddress?.toLowerCase().includes(lowercaseSearch);
        const amountMatch = tx.amount.toString().includes(lowercaseSearch);
        const currencyMatch = getCurrencyLabel(tx.currency).toLowerCase().includes(lowercaseSearch);
        return senderMatch || receiverMatch || amountMatch || currencyMatch;
      });
    }

    // Transaction type filter
    if (transactionTypeFilter !== 'all') {
      filtered = filtered.filter((tx) => {
        switch (transactionTypeFilter) {
          case 'credit':
            return tx.type === 0;
          case 'debit':
            return tx.type === 1;
          case 'internal':
            return tx.type === 2;
          default:
            return true;
        }
      });
    }

    // Date range filter
    if (dateRangeFilter !== 'all') {
      const now = new Date();
      const cutoffDate = new Date();
      
      switch (dateRangeFilter) {
        case '7days':
          cutoffDate.setDate(now.getDate() - 7);
          break;
        case '30days':
          cutoffDate.setDate(now.getDate() - 30);
          break;
        case '90days':
          cutoffDate.setDate(now.getDate() - 90);
          break;
      }

      filtered = filtered.filter((tx) => {
        const txDate = new Date(tx.createdAt);
        return txDate >= cutoffDate;
      });
    }

    // Status filter
    if (statusFilter !== 'all') {
      filtered = filtered.filter((tx) => {
        switch (statusFilter) {
          case 'pending':
            return tx.status === 0;
          case 'successful':
            return tx.status === 1;
          case 'failed':
            return tx.status === 2;
          default:
            return true;
        }
      });
    }

    return filtered;
  }, [transactions, searchTerm, transactionTypeFilter, dateRangeFilter, statusFilter]);

  if (meLoading || isLoading) {
    return <LoadingSpinner size={64} backdropOpacity={70} />;
  }

  // Open export dialog
  const openExportDialog = (format: 'csv' | 'pdf') => {
    setExportFormat(format);
    setExportDialogOpen(true);
  };

  // Handle export with selected date range
  const handleExport = async () => {
    try {
      const { startDate: exportStart, endDate: exportEnd } = getExportDateRange(
        exportDateRange, 
        customStartDate, 
        customEndDate
      );

      if (exportFormat === 'csv') {
        // You'll need to modify your useExportToCSV hook to accept startDate and endDate parameters
        // For now, using the existing refetch method
        await refetchCSV();
        
        if (csvData) {
          const blob = new Blob([csvData], { type: 'text/csv' });
          const url = window.URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `transactions_${exportStart || 'all'}_to_${exportEnd || 'now'}.csv`;
          document.body.appendChild(a);
          a.click();
          document.body.removeChild(a);
          window.URL.revokeObjectURL(url);
        }
      } else {
        await refetchPDF();
        
        if (pdfData) {
          const blob = new Blob([pdfData], { type: 'application/pdf' });
          const url = window.URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `transactions_${exportStart || 'all'}_to_${exportEnd || 'now'}.pdf`;
          document.body.appendChild(a);
          a.click();
          document.body.removeChild(a);
          window.URL.revokeObjectURL(url);
        }
      }

      setExportDialogOpen(false);
    } catch (error) {
      console.error(`Error exporting ${exportFormat.toUpperCase()}:`, error);
      alert(`Failed to export ${exportFormat.toUpperCase()}. Please try again.`);
    }
  };

  // Clear all filters
  const clearFilters = () => {
    setSearchTerm("");
    setTransactionTypeFilter('all');
    setDateRangeFilter('all');
    setStatusFilter('all');
  };

  // Active filters count
  const activeFiltersCount = [
    searchTerm ? 1 : 0,
    transactionTypeFilter !== 'all' ? 1 : 0,
    dateRangeFilter !== 'all' ? 1 : 0,
    statusFilter !== 'all' ? 1 : 0
  ].reduce((a, b) => a + b, 0);

  return (
    <DashboardLayout>
      <div className="p-6 space-y-6">
        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-bold">Transaction History</h1>
          {activeFiltersCount > 0 && (
            <Button variant="outline" size="sm" onClick={clearFilters}>
              Clear Filters ({activeFiltersCount})
            </Button>
          )}
        </div>

        <Card>
          <CardHeader className="pb-2">
            <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
              <CardTitle>
                All Transactions ({filteredTransactions.length})
              </CardTitle>

              <div className="flex flex-col sm:flex-row gap-3">
                <div className="relative">
                  <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-gray-500" />
                  <Input
                    type="search"
                    placeholder="Search transactions..."
                    className="pl-8 w-full"
                    value={searchTerm}
                    onChange={(e) => setSearchTerm(e.target.value)}
                  />
                </div>
                <div className="flex gap-2">
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button variant="outline" size="sm" className="gap-1">
                        <Filter className="h-4 w-4" />
                        Filter
                        {activeFiltersCount > 0 && (
                          <span className="ml-1 bg-blue-500 text-white text-xs rounded-full w-5 h-5 flex items-center justify-center">
                            {activeFiltersCount}
                          </span>
                        )}
                        <ChevronDown className="h-3 w-3" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end" className="w-[200px]">
                      <DropdownMenuLabel>Transaction Type</DropdownMenuLabel>
                      <DropdownMenuSeparator />
                      <DropdownMenuCheckboxItem
                        checked={transactionTypeFilter === 'all'}
                        onCheckedChange={() => setTransactionTypeFilter('all')}
                      >
                        All Transactions
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuCheckboxItem
                        checked={transactionTypeFilter === 'credit'}
                        onCheckedChange={() => setTransactionTypeFilter('credit')}
                      >
                        Received Only
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuCheckboxItem
                        checked={transactionTypeFilter === 'debit'}
                        onCheckedChange={() => setTransactionTypeFilter('debit')}
                      >
                        Sent Only
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuCheckboxItem
                        checked={transactionTypeFilter === 'internal'}
                        onCheckedChange={() => setTransactionTypeFilter('internal')}
                      >
                        Internal Only
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuSeparator />
                      <DropdownMenuLabel>Date Range</DropdownMenuLabel>
                      <DropdownMenuCheckboxItem
                        checked={dateRangeFilter === 'all'}
                        onCheckedChange={() => setDateRangeFilter('all')}
                      >
                        All Time
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuCheckboxItem
                        checked={dateRangeFilter === '7days'}
                        onCheckedChange={() => setDateRangeFilter('7days')}
                      >
                        Last 7 days
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuCheckboxItem
                        checked={dateRangeFilter === '30days'}
                        onCheckedChange={() => setDateRangeFilter('30days')}
                      >
                        Last 30 days
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuCheckboxItem
                        checked={dateRangeFilter === '90days'}
                        onCheckedChange={() => setDateRangeFilter('90days')}
                      >
                        Last 90 days
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuSeparator />
                      <DropdownMenuLabel>Status</DropdownMenuLabel>
                      <DropdownMenuCheckboxItem
                        checked={statusFilter === 'all'}
                        onCheckedChange={() => setStatusFilter('all')}
                      >
                        All Status
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuCheckboxItem
                        checked={statusFilter === 'pending'}
                        onCheckedChange={() => setStatusFilter('pending')}
                      >
                        Pending
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuCheckboxItem
                        checked={statusFilter === 'successful'}
                        onCheckedChange={() => setStatusFilter('successful')}
                      >
                        Successful
                      </DropdownMenuCheckboxItem>
                      <DropdownMenuCheckboxItem
                        checked={statusFilter === 'failed'}
                        onCheckedChange={() => setStatusFilter('failed')}
                      >
                        Failed
                      </DropdownMenuCheckboxItem>
                    </DropdownMenuContent>
                  </DropdownMenu>

                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button variant="outline" size="sm" className="gap-1">
                        <Download className="h-4 w-4" />
                        Export
                        <ChevronDown className="h-3 w-3" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end" className="w-[160px]">
                      <DropdownMenuItem onClick={() => openExportDialog('csv')}>
                        Export as CSV
                      </DropdownMenuItem>
                      <DropdownMenuItem onClick={() => openExportDialog('pdf')}>
                        Export as PDF
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </div>
            </div>
          </CardHeader>
          <CardContent>
            <div className="rounded-md border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Type</TableHead>
                    <TableHead>Amount(₦)</TableHead>
                    <TableHead>Amount</TableHead>
                    <TableHead>Asset</TableHead>
                    <TableHead>Date</TableHead>
                    <TableHead>Status</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {filteredTransactions.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={5} className="text-center py-8 text-gray-500">
                        {transactions.length === 0 
                          ? "No transactions found" 
                          : "No transactions match your filters"}
                      </TableCell>
                    </TableRow>
                  ) : (
                    filteredTransactions.map((tx: Transaction) => {
                      const isCredit = tx.type === 0;
                      const isDebit = tx.type === 1;
                      const isInternal = tx.type === 2;

                      // Icons & colors
                      let Icon = ArrowDownLeft;
                      let bg = "bg-green-100";
                      let ic = "text-green-500";
                      let label =
                        isCredit ? `From ${tx.senderAddress}` :
                        isDebit  ? `To ${tx.receiverAddress}` :
                                   `${tx.senderAddress} → ${tx.receiverAddress}`;

                      if (isDebit) {
                        Icon = ArrowUpRight;
                        bg = "bg-red-100";
                        ic = "text-red-500";
                      } else if (isInternal) {
                        bg = "bg-blue-100";
                        ic = "text-blue-500";
                      }

                      // Date & time
                      const date = tx.createdAt.substring(0, 10);
                      const time = new Date(tx.createdAt).toLocaleTimeString();

                      return (
                        <TableRow key={tx.id}>
                          <TableCell>
                            <div className="flex items-center">
                              <div
                                className={`w-8 h-8 rounded-full flex items-center justify-center mr-2 ${bg}`}
                              >
                                <Icon className={`h-4 w-4 ${ic}`} />
                              </div>
                              <span className="capitalize">{label}</span>
                            </div>
                          </TableCell>
                          <TableCell
                            className={`font-medium ${
                              isCredit
                                ? "text-green-500"
                                : isDebit
                                ? "text-red-500"
                                : ""
                            }`}
                          >
                            {isCredit ? "+" : isDebit ? "-" : ""}
                            {`₦${(+tx.amount * ethToNairaRate).toLocaleString("en-NG", {
                              minimumFractionDigits: 2,
                              maximumFractionDigits: 8,
                            })}`}
                          </TableCell>
                          <TableCell
                            className={`font-medium ${
                              isCredit
                                ? "text-green-500"
                                : isDebit
                                ? "text-red-500"
                                : ""
                            }`}
                          >
                            {isCredit ? "+" : isDebit ? "-" : ""}
                            {` ${(+tx.amount).toLocaleString("en-NG", {
                              minimumFractionDigits: 2,
                              maximumFractionDigits: 8,
                            })}`}
                          </TableCell>
                          <TableCell>
                            {getCurrencyLabel(tx.currency)}
                          </TableCell>
                          <TableCell>
                            <div>{date}</div>
                            <div className="text-xs text-gray-500">{time}</div>
                          </TableCell>
                          <TableCell>
                            <span
                              className={`inline-flex items-center rounded-full px-2 py-1 text-xs font-medium ${getStatusStyles(tx.status)}`}
                            >
                              {getStatusLabel(tx.status)}
                            </span>
                          </TableCell>
                        </TableRow>
                      );
                    })
                  )}
                </TableBody>
              </Table>
            </div>
          </CardContent>
        </Card>

        {/* Export Dialog */}
        <Dialog open={exportDialogOpen} onOpenChange={setExportDialogOpen}>
          <DialogContent className="sm:max-w-[500px]">
            <DialogHeader>
              <DialogTitle className="flex items-center gap-2">
                <Download className="h-5 w-5" />
                Export Transactions as {exportFormat.toUpperCase()}
              </DialogTitle>
              <DialogDescription>
                Choose the date range for your export. All transactions within the selected period will be included.
              </DialogDescription>
            </DialogHeader>
            
            <div className="space-y-4 py-4">
              <div className="space-y-2">
                <Label>Date Range</Label>
                <div className="grid grid-cols-2 gap-2">
                  {[
                    { value: 'all', label: 'All Time' },
                    { value: 'today', label: 'Today' },
                    { value: 'yesterday', label: 'Yesterday' },
                    { value: '7days', label: 'Last 7 Days' },
                    { value: '30days', label: 'Last 30 Days' },
                    { value: '90days', label: 'Last 90 Days' },
                    { value: 'thisMonth', label: 'This Month' },
                    { value: 'lastMonth', label: 'Last Month' },
                    { value: 'thisYear', label: 'This Year' },
                    { value: 'lastYear', label: 'Last Year' },
                    { value: 'custom', label: 'Custom Range' },
                  ].map((option) => (
                    <Button
                      key={option.value}
                      variant={exportDateRange === option.value ? "default" : "outline"}
                      size="sm"
                      onClick={() => setExportDateRange(option.value as ExportDateRange)}
                      className="justify-start"
                    >
                      {option.label}
                    </Button>
                  ))}
                </div>
              </div>

              {exportDateRange === 'custom' && (
                <div className="space-y-3">
                  <div className="grid grid-cols-2 gap-3">
                    <div className="space-y-2">
                      <Label htmlFor="start-date">Start Date</Label>
                      <Input
                        id="start-date"
                        type="date"
                        value={customStartDate}
                        onChange={(e) => setCustomStartDate(e.target.value)}
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="end-date">End Date</Label>
                      <Input
                        id="end-date"
                        type="date"
                        value={customEndDate}
                        onChange={(e) => setCustomEndDate(e.target.value)}
                      />
                    </div>
                  </div>
                </div>
              )}

              <div className="p-3 bg-gray-50 rounded-lg">
                <div className="flex items-center gap-2 text-sm">
                  <Calendar className="h-4 w-4 text-gray-500" />
                  <span className="text-gray-700">
                    <strong>Selected Range:</strong> {getExportDateRangeLabel(exportDateRange)}
                  </span>
                </div>
                {exportDateRange === 'custom' && (customStartDate || customEndDate) && (
                  <div className="mt-1 text-xs text-gray-500">
                    {customStartDate} {customStartDate && customEndDate ? 'to' : ''} {customEndDate}
                  </div>
                )}
              </div>
            </div>

            <DialogFooter>
              <Button variant="outline" onClick={() => setExportDialogOpen(false)}>
                Cancel
              </Button>
              <Button 
                onClick={handleExport}
                disabled={
                  (csvLoading && exportFormat === 'csv') || 
                  (pdfLoading && exportFormat === 'pdf') ||
                  (exportDateRange === 'custom' && (!customStartDate || !customEndDate))
                }
              >
                {((csvLoading && exportFormat === 'csv') || (pdfLoading && exportFormat === 'pdf')) 
                  ? 'Exporting...' 
                  : `Export ${exportFormat.toUpperCase()}`}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>
    </DashboardLayout>
  );
}