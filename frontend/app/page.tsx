import { Button } from "@/components/ui/button"
import Link from "next/link"

export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center p-4 bg-linear-to-b from-pink-50 to-pink-100">
      <div className="max-w-md w-full text-center space-y-8">
        <div className="space-y-2">
          <div className="flex justify-center">
            <div className="bg-white p-4 rounded-full shadow-md">
              <svg
                xmlns="http://www.w3.org/2000/svg"
                width="64"
                height="64"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
                className="text-pink-500"
              >
                <path d="M19 5c-1.5 0-2.8 1.4-3 2-3.5-1.5-11-.3-11 5 0 1.8 0 3 2 4.5V20h4v-2h3v2h4v-4c1-.5 1.7-1 2-2h2v-4h-2c0-1-.5-1.5-1-2h0V5z" />
                <path d="M2 9v1c0 1.1.9 2 2 2h1" />
                <path d="M16 11h0" />
              </svg>
            </div>
          </div>
          <h1 className="text-3xl font-bold tracking-tighter">PiggyPal</h1>
          <p className="text-gray-500">Your friendly crypto wallet</p>
        </div>
        <div className="space-y-4">
          <Button asChild className="w-full bg-pink-500 hover:bg-pink-600">
            <Link href="/login">Login</Link>
          </Button>
          <Button asChild variant="outline" className="w-full">
            <Link href="/register">Create Account</Link>
          </Button>
        </div>
      </div>
    </main>
  )
}
