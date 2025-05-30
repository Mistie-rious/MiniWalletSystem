import { useQuery } from "@tanstack/react-query";
import { fetchMe, UserDto } from "@/api/routes/user";
import { toast } from "react-toastify";
import { useEffect } from "react";
import Error from "next/error";
import { AxiosError } from 'axios';
import { useRouter } from "next/navigation";

export const useMe = () => {
  const router = useRouter();
  
  // Get userId from sessionStorage to make query key user-specific
  const userId = typeof window !== 'undefined' ? sessionStorage.getItem("userId") : null;
  
  // Standard React Query setup with user-specific query key
  const query = useQuery<UserDto, AxiosError>({
    queryKey: ["me", userId], // Include userId in query key
    queryFn: fetchMe,
    staleTime: 1000 * 60 * 10, // 10 minutes
    enabled: !!userId, // Only run query if we have a userId
    retry: (failureCount, error) => {
      // Don't retry on 401 errors
      if (error?.response?.status === 401) {
        return false;
      }
      // Otherwise retry once
      return failureCount < 1;
    },
  });

  // Add an effect to handle 401 errors
  useEffect(() => {
    // Check if there's an error and it's a 401
    if (query.error && query.error.response?.status === 401) {
      // Clear auth data
      sessionStorage.removeItem("jwtToken");
      sessionStorage.removeItem("userId");
      sessionStorage.removeItem("userEmail");
      
      // Show error toast
      toast.error("Your session has expired. Please log in again.");
      
      // Redirect to login
      router.push("/login");
    }
  }, [query.error, router]);

  return query;
};