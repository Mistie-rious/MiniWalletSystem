import type React from "react"
import type { Metadata } from "next"
import { Inter } from "next/font/google"
import "./globals.css"
import { ThemeProvider } from "@/components/theme-provider"
import { Toaster } from "@/components/ui/toaster"
import { Providers } from "@/components/providers/Providers"

const inter = Inter({ subsets: ["latin"] })

export const metadata: Metadata = {
  title: "PiggyPal - Your Crypto Wallet",
  description: "A modern crypto wallet for easy transactions",
    generator: 'v0.dev'
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return (
    <html lang="en"
    className="light"                 // ← match your defaultTheme
    style={{ colorScheme: 'light' }} >
      <body className={inter.className}>
      <Providers>

          {children}
          
  
        </Providers>
   
      </body>
    </html>
  )
}
