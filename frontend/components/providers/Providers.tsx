"use client";

import { AuthProvider } from "./auth-provider";
import { ReactNode } from "react";
import {  QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider } from "../theme-provider";
import queryClient from "@/services/queryClient";


export function Providers({ children }: { children: ReactNode }) {
  return   <QueryClientProvider client={queryClient}>
  <AuthProvider>
    <ThemeProvider attribute="class" defaultTheme="light" enableSystem disableTransitionOnChange>
  
 
    {children}
 

    </ThemeProvider>
    </AuthProvider>
    </QueryClientProvider>
}
