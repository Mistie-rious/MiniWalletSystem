"use client"

import { useState } from "react"
import { Card, CardContent, CardHeader, CardTitle, CardFooter } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { useUnlockWallet } from "@/hooks/useTransactions"
import { useMe } from "@/hooks/useMe"
import { toast } from "react-toastify"
import { Loader2, Lock, Unlock, Eye, EyeOff } from "lucide-react"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog"

export function UnlockWalletDialog({ 
  children, 
  onUnlockSuccess 
}: { 
  children: React.ReactNode; 
  onUnlockSuccess?: () => void;
}) {
  const [isOpen, setIsOpen] = useState(false);
  
  const handleUnlockSuccess = () => {
    setIsOpen(false);
    if (onUnlockSuccess) {
      onUnlockSuccess();
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={setIsOpen}>
      <DialogTrigger asChild>
        {children}
      </DialogTrigger>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Unlock Your Wallet</DialogTitle>
        </DialogHeader>
        <UnlockWalletForm onSuccess={handleUnlockSuccess} />
      </DialogContent>
    </Dialog>
  );
}

export function UnlockWalletForm({ onSuccess }: { onSuccess?: () => void }) {
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const { data: me } = useMe();
  const walletId = me?.data.walletId ?? "";
  
  const { mutate: unlockWallet, isPending } = useUnlockWallet();

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!password) {
      toast.error("Please enter your password");
      return;
    }

    if (!walletId) {
      toast.error("Wallet ID not found");
      return;
    }

    // Show processing toast
    toast.info("Unlocking wallet...", { autoClose: false, toastId: "unlocking-wallet" });

    unlockWallet(
      {
        walletId,
        password
      },
      {
        onSuccess: (data) => {
          toast.dismiss("unlocking-wallet");
          toast.success("Wallet unlocked successfully");
          setPassword("");
          if (onSuccess) {
            onSuccess();
          }
        },
        onError: (error) => {
          toast.dismiss("unlocking-wallet");
          toast.error(`Failed to unlock wallet: Invalid password`);
        }
      }
    );
  };

  return (
    <form onSubmit={handleSubmit}>
      <div className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="password">Wallet Password</Label>
          <div className="relative">
            <Input
              id="password"
              type={showPassword ? "text" : "password"}
              placeholder="Enter your wallet password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="pr-10"
            />
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="absolute right-0 top-0 h-full"
              onClick={() => setShowPassword(!showPassword)}
            >
              {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </Button>
          </div>
        </div>
        
        <Button 
          type="submit" 
          className="w-full bg-pink-500 hover:bg-pink-600"
          disabled={isPending || !walletId}
        >
          {isPending ? (
            <>
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              Unlocking...
            </>
          ) : (
            <>
              <Unlock className="mr-2 h-4 w-4" />
              Unlock Wallet
            </>
          )}
        </Button>
      </div>
    </form>
  );
}

export function UnlockWalletCard({ onSuccess }: { onSuccess?: () => void }) {
  return (
    <Card className="border-none shadow-lg">
      <CardHeader>
        <CardTitle className="flex items-center">
          <Lock className="mr-2 h-5 w-5 text-pink-500" />
          Unlock Your Wallet
        </CardTitle>
      </CardHeader>
      <CardContent>
        <UnlockWalletForm onSuccess={onSuccess} />
      </CardContent>
      <CardFooter className="justify-center text-sm text-gray-500">
        <p>Enter your password to access wallet functions</p>
      </CardFooter>
    </Card>
  );
}