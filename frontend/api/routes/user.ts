import apiClient from "../apiClient";

export interface UserDto {
    data: {
        id: string;
        email: string;
        username: string;
        walletId: string;
        walletAddress: string;
    }
  }

  export const fetchMe = async (): Promise<UserDto> => {
    const { data } = await apiClient.get<UserDto>("/auth/me");
   
    return data;
  };