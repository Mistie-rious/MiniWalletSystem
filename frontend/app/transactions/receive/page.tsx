"use client"

import { DashboardLayout } from "@/components/dashboard-layout"
import { Card, CardContent } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { ArrowDownLeft } from "lucide-react"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { useSendMoney } from "@/hooks/useTransactions"
import { useMe } from "@/hooks/useMe"
import { UnlockWalletDialog } from "@/components/UnlockWallet"
import { useBalanceWallet } from "@/hooks/useTransactions"
import Link from "next/link"
import { Input } from "@/components/ui/input"
import { toast , ToastContainer} from "react-toastify"
import { useRouter } from "next/navigation"
import LoadingSpinner from "@/components/loadingSpinner"
import { ProtectedRoute } from "@/components/protectedRoute"
export default function Receive() {
  const router = useRouter()
  const { data: me, isLoading: meLoading, error: meError } = useMe();
  const myAddresses = me?.data.walletAddress!;
  const displayAddress = myAddresses
  ? `${myAddresses.slice(0, 20)}...${myAddresses.slice(-4)}`
  : ""
  console.log("myAddresses", myAddresses);


if (meLoading){
  return <LoadingSpinner size={80} backdropOpacity={70} />
}


  return (
    <ProtectedRoute>
    <DashboardLayout>
      <div className="p-6 flex justify-center">
        <div className="w-full max-w-md">
          <div className="mb-6">
            <h1 className="text-2xl font-bold">Transfer Funds</h1>
            <p className="text-gray-500">Send, transfer, or receive crypto</p>
          </div>

          <Tabs defaultValue="receive" className="w-full">
            <TabsList className="grid grid-cols-2 mb-6">
              <TabsTrigger value="send" asChild>
                <Link href="/transactions/send">Send</Link>
              </TabsTrigger>
              {/* <TabsTrigger value="transfer" asChild>
                <Link href="/transactions/transfer">Transfer</Link>
              </TabsTrigger> */}
              <TabsTrigger value="receive">Receive</TabsTrigger>
            </TabsList>

            <TabsContent value="receive">
              <Card className="border-none shadow-lg">
                <CardContent className="p-6 space-y-4">
                  <div className="space-y-2">
                    <Label htmlFor="receive-to">Receive to</Label>
                    <Select defaultValue="main">
                      <SelectTrigger>
                        <SelectValue placeholder="Select wallet" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="main">Main Wallet</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  {/* <div className="flex justify-center py-6">
                    <div className="bg-gray-100 p-4 rounded-lg">
                      <div className="w-48 h-48 bg-white flex items-center justify-center border">
                        <div className="text-center">
                          <div className="text-sm text-gray-500 mb-2">QR Code</div>
                          <div className="text-xs">Scan to receive payment</div>
                        </div>
                      </div>
                    </div>
                  </div> */}

<div className="space-y-2">
                    <Label htmlFor="wallet-address">Wallet Address</Label>
                    <div className="relative">
                      <Input
                        id="wallet-address"
                        className="pr-10 truncate"
                        value={displayAddress}
                        readOnly
                      />
                      <Button
                        variant="ghost"
                        size="sm"
                        className="absolute right-1 top-1 h-7"
                        onClick={() => {
                          navigator.clipboard.writeText(myAddresses)
                          toast.info("Address copied to clipboard!")
                        }}
                      >
                        Copy
                      </Button>
                    </div>
                    <ToastContainer/>
                  </div>

                  
                </CardContent>
              </Card>
            </TabsContent>
          </Tabs>
        </div>
      </div>
    </DashboardLayout>
    </ProtectedRoute>
  )
}
