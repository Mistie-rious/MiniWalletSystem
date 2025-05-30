"use client"

import { DashboardLayout } from "@/components/dashboard-layout"
import { Card, CardContent } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { ArrowRight } from "lucide-react"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import Link from "next/link"

export default function Transfer() {
  return (
    <DashboardLayout>
      <div className="p-6 flex justify-center">
        <div className="w-full max-w-md">
          <div className="mb-6">
            <h1 className="text-2xl font-bold">Transfer Funds</h1>
            <p className="text-gray-500">Send, transfer, or receive crypto</p>
          </div>

          <Tabs defaultValue="transfer" className="w-full">
            <TabsList className="grid grid-cols-3 mb-6">
              <TabsTrigger value="send" asChild>
                <Link href="/transactions/send">Send</Link>
              </TabsTrigger>
              <TabsTrigger value="transfer">Transfer</TabsTrigger>
              <TabsTrigger value="receive" asChild>
                <Link href="/transactions/receive">Receive</Link>
              </TabsTrigger>
            </TabsList>

            <TabsContent value="transfer">
              <Card className="border-none shadow-lg">
                <CardContent className="p-6 space-y-4">
                  <div className="space-y-2">
                    <Label htmlFor="transfer-from">Transfer from</Label>
                    <Select defaultValue="main">
                      <SelectTrigger>
                        <SelectValue placeholder="Select wallet" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="main">Main Wallet (₦ 900,000)</SelectItem>
                        <SelectItem value="savings">Savings Wallet (₦ 250,000)</SelectItem>
                        <SelectItem value="business">Business Wallet (₦ 450,000)</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  <div className="space-y-2">
                    <Label htmlFor="transfer-to">Transfer to</Label>
                    <Select>
                      <SelectTrigger>
                        <SelectValue placeholder="Select destination" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="savings">Savings Wallet</SelectItem>
                        <SelectItem value="business">Business Wallet</SelectItem>
                        <SelectItem value="investment">Investment Wallet</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  <div className="space-y-2">
                    <Label htmlFor="amount">Amount</Label>
                    <div className="relative">
                      <Input id="amount" placeholder="0.00" />
                      <div className="absolute inset-y-0 right-0 flex items-center pr-3 pointer-events-none text-gray-500">
                        NGN
                      </div>
                    </div>
                  </div>

                  <Button className="w-full mt-6 bg-pink-500 hover:bg-pink-600">
                    <ArrowRight className="mr-2 h-4 w-4" />
                    Transfer Funds
                  </Button>
                </CardContent>
              </Card>
            </TabsContent>
          </Tabs>
        </div>
      </div>
    </DashboardLayout>
  )
}
