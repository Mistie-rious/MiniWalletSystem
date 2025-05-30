"use client"

import type React from "react"

import { useState, useEffect } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import Link from "next/link"
import { useRouter } from "next/navigation"
import { PiggyLogo } from "@/components/piggy-logo"
import apiClient from "@/api/apiClient"
import axios from "axios"
import { PasswordInput } from "@/components/ui/password-input"
import { useAuth } from "@/components/providers/auth-provider"
import { ToastContainer, toast } from 'react-toastify';
interface RegisterModel {
  email: string
  password: string
  username: string
}

interface ApiResponse<T> {
  success: boolean
  data?: T
  message?: string
  errors?: string[]
}

export default function Register() {
  const [name, setName] = useState("")
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [username, setUsername] = useState("")
  const [confirmPassword, setConfirmPassword] = useState("")

  const { register, isLoading, isAuthenticated } = useAuth()

  const router = useRouter()


  useEffect(() => {
    if (isAuthenticated) {
      router.push("/dashboard")
    }
  }, [isAuthenticated, router])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    if (password !== confirmPassword) {
      toast.error("Passwords do not match")
      return
    }
    
    const success = await register(email, password, username)
    if (success) { // Redirect to login after a short delay to allow the user to see the success toast
      setTimeout(() => {
        router.push("/login")
      }, 1500)
    }
  }
  
  return (
    <div className="grid md:grid-cols-2 min-h-screen">
      <div className="flex flex-col p-8 md:p-12">
        <div className="flex items-center gap-2 mb-8">
          <PiggyLogo className="h-8 w-8 text-pink-500" />
          <span className="font-bold text-xl">PiggyPal</span>
        </div>

        <div className="flex-1 flex flex-col justify-center max-w-md mx-auto w-full">
          <div className="space-y-6">
            <div className="space-y-2 text-center">
              <h1 className="text-3xl font-bold">Create an account</h1>
              <p className="text-gray-500">Enter your details to get started</p>
            </div>

            <form onSubmit={handleSubmit} className="space-y-4">
              <ToastContainer/>
              {/* <div className="space-y-2">
                <Label htmlFor="name">Name</Label>
                <Input id="name" placeholder="Enter your fullname" required  value={name} onChange={e => setName(e.target.value)}/>
              </div> */}

              <div className="space-y-2">
                <Label htmlFor="username">Username</Label>
                <Input id="username" placeholder="Create a username" required  value={username} onChange={e => setUsername(e.target.value)} />
              </div>

              <div className="space-y-2">
                <Label htmlFor="email">Email</Label>
                <Input id="email" type="email"  placeholder="Enter your email" required  value={email} onChange={e => setEmail(e.target.value)}/>
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

<div className="space-y-2">
  <Label htmlFor="confirm-password">Confirm Password</Label>
  <PasswordInput
    id="confirm-password"
    placeholder="Re-enter your password"
    required
    value={confirmPassword}
    onChange={e => setConfirmPassword(e.target.value)}
  />
</div>



              <Button type="submit"  className="w-full bg-pink-500 hover:bg-pink-600" disabled={isLoading}>
                {isLoading ? "Creating account..." : "Create account"}
              </Button>
            </form>

            <div className="text-center text-sm">
              Already have an account?{" "}
              <Link href="/login" className="text-pink-500 hover:underline">
                Login
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
  )
}
