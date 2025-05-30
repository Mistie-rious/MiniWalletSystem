"use client"

import React, { useState, useEffect } from "react"
import Link from "next/link"
import { useRouter } from "next/navigation"
import { DashboardLayout } from "@/components/dashboard-layout"
import { Card, CardContent } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { ArrowUpRight, Loader2, Lock, Repeat } from "lucide-react"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { toast, ToastContainer } from "react-toastify"
import { useSendMoney, useBalanceWallet } from "@/hooks/useTransactions"
import { useMe } from "@/hooks/useMe"
import { UnlockWalletDialog } from "@/components/UnlockWallet"
import { ProtectedRoute } from "@/components/protectedRoute"
import LoadingSpinner from "@/components/loadingSpinner"

export default function Send() {
  const UNLOCK_KEY = "walletUnlockedUntil"
  const [amount, setAmount] = useState("")
  const [convertedAmount, setConvertedAmount] = useState("")
  const [recipient, setRecipient] = useState("")
  const [description, setDescription] = useState("")
  const [selectedWallet, setSelectedWallet] = useState("main")
  const [selectedAsset, setSelectedAsset] = useState("eth")
  const [isWalletUnlocked, setIsWalletUnlocked] = useState(false)
  const [isMounted, setIsMounted] = useState(false)

  const router = useRouter()
  const { data: me, isLoading: meLoading } = useMe()
  const walletId = me?.data.walletId ?? ""
  const userId = me?.data.id ?? ""
  const senderAddress = me?.data.walletAddress ?? ""

  // Debug logging for user data
  useEffect(() => {
    if (me && isMounted) {
      console.log('User data loaded:', {
        walletId: me.data.walletId,
        userId: me.data.id,
        walletAddress: me.data.walletAddress,
        fullData: me.data
      });
    }
  }, [me, isMounted]);

  // Handle mounting to prevent hydration issues
  useEffect(() => {
    setIsMounted(true)
  }, [])

  useEffect(() => {
    if (!isMounted || !walletId) return
    
    const checkWalletStatus = () => {
      const key = `${UNLOCK_KEY}_${walletId}`
      const until = sessionStorage.getItem(key)
      console.log(`Checking wallet status - Key: ${key}, Until: ${until}, Current time: ${Date.now()}`)
      
      if (until && Date.now() < Number(until)) {
        console.log('Wallet is unlocked')
        setIsWalletUnlocked(true)
      } else {
        console.log('Wallet is locked or expired')
        sessionStorage.removeItem(key)
        setIsWalletUnlocked(false)
      }
    }

    checkWalletStatus()
    
    // Check wallet status every 30 seconds
    const interval = setInterval(checkWalletStatus, 30000)
    
    return () => clearInterval(interval)
  }, [walletId, isMounted])

  // Listen for wallet unlock events from other components
  useEffect(() => {
    if (!isMounted) return

    const handleWalletUnlock = (event: CustomEvent) => {
      console.log('Received wallet unlock event:', event.detail)
      if (event.detail.walletId === walletId) {
        setIsWalletUnlocked(true)
      }
    }

    window.addEventListener('walletUnlocked', handleWalletUnlock as EventListener)
    
    return () => {
      window.removeEventListener('walletUnlocked', handleWalletUnlock as EventListener)
    }
  }, [walletId, isMounted])

  const { data: walletData, isLoading: walletLoading } = useBalanceWallet(userId)
  const { mutate: sendMoney, isPending } = useSendMoney()

  const ethToNairaRate = 4500000
  const nairaBalanceRaw = (walletData?.balance ?? 0) * ethToNairaRate
  const nairaBalance = `₦${nairaBalanceRaw.toLocaleString("en-NG", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`

  const handleAmountChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const value = e.target.value
    setAmount(value)
    const num = parseFloat(value.replace(/,/g, ""))
    if (!isNaN(num)) {
      setConvertedAmount((ethToNairaRate * num).toFixed(2))
    } else {
      setConvertedAmount("")
    }
  }

  const handleSendFunds = () => {
    console.log('handleSendFunds called, wallet unlocked:', isWalletUnlocked)
    
    if (!isWalletUnlocked) {
      toast.error("Wallet is locked. Please unlock your wallet first.")
      return
    }
    
    // Enhanced validation
    if (!recipient) return toast.error("Please enter a recipient address or username")
    if (!senderAddress) return toast.error("Sender address is missing")
    if (!walletId) return toast.error("Wallet ID is missing")
    
    const num = parseFloat(amount.replace(/,/g, ""))
    if (isNaN(num)) return toast.error("Please enter a valid amount")
    if (num <= 0) return toast.error("Amount must be greater than 0")

    // Verify wallet is still unlocked before sending
    const key = `${UNLOCK_KEY}_${walletId}`
    const until = sessionStorage.getItem(key)
    if (!until || Date.now() >= Number(until)) {
      setIsWalletUnlocked(false)
      toast.error("Wallet has expired. Please unlock it again.")
      return
    }

    // Log the parameters for debugging
    console.log("Send parameters:", {
      senderAddress,
      amount: num,
      walletId,
      receiverAddress: recipient,
      description: description || `Sent ${amount} ETH to ${recipient}`,
      currency: 0,
    })

    toast.info("Processing your transaction...", { autoClose: false, toastId: "processing-transaction" })

    sendMoney(
      {
        senderAddress,
        amount: num,
        walletId,
        receiverAddress: recipient,
        description: description || `Sent ${amount} ETH to ${recipient}`,
        currency: 0,
      },
      {
        onSuccess: () => {
          toast.dismiss("processing-transaction")
          toast.success("Transaction completed successfully!")
          setAmount("")
          setConvertedAmount("")
          setRecipient("")
          setDescription("")
          setIsWalletUnlocked(false)
          
          // Clear the unlock session
          sessionStorage.removeItem(key)
        },
        onError: (error) => {
          toast.dismiss("processing-transaction")
          console.error("Transaction error:", error)
          
          // Enhanced error handling
          let errorMessage = "Unknown error"
          if (error instanceof Error) {
            errorMessage = error.message
          } else if (typeof error === 'string') {
            errorMessage = error
          } else if (error && typeof error === 'object' && 'message' in error) {
            errorMessage = String(error)
          }
          
          toast.error(`Transaction failed: ${errorMessage}`)
        },
      }
    )
  }

  const handleWalletUnlocked = () => {
    if (!isMounted || !walletId) return
    
    console.log('Wallet unlock successful, setting expiry...')
    const key = `${UNLOCK_KEY}_${walletId}`
    const expiry = Date.now() + 30 * 60 * 1000 // 3 minutes
    sessionStorage.setItem(key, String(expiry))
    setIsWalletUnlocked(true)
    
    // Dispatch custom event to notify other components
    const event = new CustomEvent('walletUnlocked', {
      detail: { walletId, expiry }
    })
    window.dispatchEvent(event)
    
    console.log(`Wallet unlocked until: ${new Date(expiry).toLocaleTimeString()}`)
    
    // Don't auto-send, let user click the button
    // setTimeout(handleSendFunds, 100)
  }

  if (meLoading || !isMounted) {
    return <LoadingSpinner size={60} backdropOpacity={70} />
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

            <Tabs defaultValue="send" className="w-full">
              <TabsList className="grid grid-cols-2 mb-6">
                <TabsTrigger value="send">Send</TabsTrigger>
                <TabsTrigger value="receive" asChild>
                  <Link href="/transactions/receive">Receive</Link>
                </TabsTrigger>
              </TabsList>

              <TabsContent value="send">
                <Card className="border-none shadow-lg">
                  <CardContent className="p-6 space-y-4">
                    <div className="space-y-2">
                      <Label htmlFor="from-wallet">Send from</Label>
                      <Select value={selectedWallet} onValueChange={setSelectedWallet}>
                        <SelectTrigger>
                          <SelectValue placeholder="Select wallet" />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="main">Main Wallet ({nairaBalance})</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>

                    <div className="space-y-2">
                      <Label htmlFor="recipient">Send to</Label>
                      <Input
                        id="recipient"
                        placeholder="Enter wallet address or username"
                        value={recipient}
                        onChange={(e) => setRecipient(e.target.value)}
                      />
                    </div>

                    <div className="space-y-2">
                      <Label htmlFor="asset">Asset</Label>
                      <Select value={selectedAsset} onValueChange={setSelectedAsset}>
                        <SelectTrigger>
                          <SelectValue placeholder="Select asset" />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="eth">Ethereum (ETH)</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>

                    <div className="space-y-2">
                      <Label htmlFor="amount">Amount</Label>
                      <div className="relative">
                        <Input id="amount" placeholder="0.00" value={amount} onChange={handleAmountChange} />
                        <div className="absolute inset-y-0 right-0 flex items-center pr-3 pointer-events-none text-gray-500">ETH</div>
                      </div>
                      {convertedAmount && (
                        <div className="flex items-center justify-between text-sm text-gray-500 mt-1">
                          <span>Conversion</span>
                          <div className="flex items-center">
                            <span>{convertedAmount} NGN</span>
                            <Repeat className="h-3 w-3 ml-1" />
                          </div>
                        </div>
                      )}
                    </div>

                    <div className="space-y-2">
                      <Label htmlFor="description">Description (Optional)</Label>
                      <Input
                        id="description"
                        placeholder="What's this payment for?"
                        value={description}
                        onChange={(e) => setDescription(e.target.value)}
                      />
                    </div>

                    {isWalletUnlocked ? (
                      <>
                        <div className="flex items-center justify-between text-sm text-green-600 mb-2">
                          <span>✓ Wallet Unlocked</span>
                          <button 
                            onClick={() => {
                              const key = `${UNLOCK_KEY}_${walletId}`
                              const until = sessionStorage.getItem(key)
                              if (until) {
                                const remaining = Math.max(0, Number(until) - Date.now())
                                const minutes = Math.floor(remaining / 60000)
                                const seconds = Math.floor((remaining % 60000) / 1000)
                                console.log(`Time remaining: ${minutes}:${seconds.toString().padStart(2, '0')}`)
                              }
                            }}
                            className="text-xs underline"
                          >
                            Check Time
                          </button>
                        </div>
                        <Button className="w-full mt-6 bg-pink-500 hover:bg-pink-600" onClick={handleSendFunds} disabled={isPending || meLoading || !walletId}>
                          {isPending ? (
                            <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Processing...</>
                          ) : (
                            <><ArrowUpRight className="mr-2 h-4 w-4" />Send Funds</>
                          )}
                        </Button>
                        <ToastContainer/>
                      </>
                    ) : (
                      <>
                        <UnlockWalletDialog onUnlockSuccess={handleWalletUnlocked}>
                          <Button className="w-full mt-6 bg-pink-500 hover:bg-pink-600" disabled={meLoading || !walletId}>
                            <Lock className="mr-2 h-4 w-4" />Unlock Wallet to Send
                          </Button>
                        </UnlockWalletDialog>
                        <ToastContainer />
                      </>
                    )}
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