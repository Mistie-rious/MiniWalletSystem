"use client"

import React, { useState, useEffect } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import Link from "next/link"
import { useRouter } from "next/navigation"
import { PiggyLogo } from "@/components/piggy-logo"
import { ToastContainer, toast } from 'react-toastify'
import { useAuth } from "@/components/providers/auth-provider"
import LoadingSpinner from "@/components/loadingSpinner"
import { PasswordInput } from "@/components/ui/password-input"
interface LoginModel {
  email: string
  password: string
}

export default function Login() {
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const { login, isLoading, isAuthenticated } = useAuth()
  const router = useRouter()

  useEffect(() => {

    if (!isLoading && isAuthenticated) {
      console.log("User already authenticated, redirecting to dashboard");
      router.push("/dashboard");
    }
  }, [isLoading, isAuthenticated, router]);

  const handleSubmit = async (e: any) => {
    e.preventDefault();
    
    // Prevent multiple submissions
    if (isLoading) return;
    
    const success = await login(email, password);
    if (success) {
      // Successfully logged in, navigate to dashboard
      // The useEffect will handle this redirect automatically
      console.log("Login successful");
    }
  };

  if (isLoading) {
    return (
      <div className="flex justify-center items-center min-h-screen">
        <LoadingSpinner />
      </div>
    );
  }

  // Don't show login form if already authenticated
  if (isAuthenticated) {
    return (
      <div className="flex justify-center items-center min-h-screen">
        <LoadingSpinner />
        <p>Already logged in. Redirecting...</p>
      </div>
    );
  }

  return (
    <>
      {isLoading && <LoadingSpinner size={80} backdropOpacity={70} />}
      <div className="grid md:grid-cols-2 min-h-screen">
        <div className="flex flex-col p-8 md:p-12">
          <div className="flex items-center gap-2 mb-8">
            <PiggyLogo className="h-8 w-8 text-pink-500" />
            <span className="font-bold text-xl">PiggyPal</span>
          </div>

          <div className="flex-1 flex flex-col justify-center max-w-md mx-auto w-full">
            <div className="space-y-6">
              <div className="space-y-2 text-center">
                <h1 className="text-3xl font-bold">Welcome back</h1>
                <p className="text-gray-500">Enter your credentials to access your account</p>
              </div>

              <form onSubmit={handleSubmit} className="space-y-4">
                <ToastContainer />

                <div className="space-y-2">
                  <Label htmlFor="email">Email</Label>
                  <Input
                    id="email"
                    type="email"
                    placeholder="Enter your email"
                    required
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                  />
                </div>

                <div className="space-y-2">
  <Label htmlFor="password">Password</Label>
  <PasswordInput
    id="password"
    placeholder="Create a password"
    required
    value={password}
    onChange={e => setPassword(e.target.value)}
  />
</div>

                <div className="text-right">
                  <Link href="#" className="text-sm text-pink-500 hover:underline">
                    Forgot password?
                  </Link>
                </div>

                <Button
                  type="submit"
                  className="w-full bg-pink-500 hover:bg-pink-600"
                  disabled={isLoading}
                >
                  {isLoading ? "Logging in..." : "Login"}
                </Button>
              </form>

              <div className="text-center text-sm">
                Don't have an account?{' '}
                <Link href="/register" className="text-pink-500 hover:underline">
                  Create account
                </Link>
              </div>
            </div>
          </div>
        </div>

        <div className="hidden md:block bg-linear-to-br from-pink-400 to-pink-600">
          <div className="h-full flex items-center justify-center p-8">
            <div className="max-w-md text-white">
              <h2 className="text-3xl font-bold mb-4">Manage your crypto with ease</h2>
              <p className="opacity-90">
                PiggyPal provides a simple and secure way to manage your cryptocurrency. Send, receive, and track your
                assets all in one place.
              </p>
            </div>
          </div>
        </div>
      </div>
    </>
  )
}
