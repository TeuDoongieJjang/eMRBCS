using eMRBCS.Global;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using eMRBCS.Models;
using Microsoft.Maui;

namespace eMRBCS.Services
{
    internal class GridService
    {
        List<Grid> gridConsumersUnbilled;
        List<Grid> gridConsumersBilled;

        Grid tmpgridConsumerUnbilled;
        Grid tmpgridConsumerBilled;
        

        int rowindex_unbilled = 0;
        int rowindex_billed = 0;
        int batchno_u = 0;
        int batchno_b = 0;

        int noofrecords = 0;

        private ICommand FrameTappedCommand { get; set; }

        private void OnFrameTapped(string parameter)
        {
            ConsumerRecords.acctNo = parameter;
        }

        public async Task<bool> ConsumerToGrid(List<Consumers> consumers)
        {
            bool isDone = true;
            await Task.Run(() =>
            {
                //consumers.OrderBy(c => c.seqno).ToList();
                gridConsumersUnbilled = new List<Grid>();
                gridConsumersBilled = new List<Grid>();

                //tmpgridConsumerUnbilled = new Grid
                //{
                //    AutomationId = $"UGrid_{batchno_u}",
                //    RowDefinitions =
                //    {
                //        new RowDefinition { Height = GridLength.Auto }
                //    },
                //    ColumnDefinitions =
                //    {
                //        new ColumnDefinition { Width = GridLength.Star }
                //    }
                //};

                

                //tmpgridConsumerBilled = new Grid
                //{
                //    AutomationId = $"BGrid_{batchno_b}",
                //    RowDefinitions =
                //    {
                //        new RowDefinition { Height = GridLength.Auto }
                //    },
                //    ColumnDefinitions =
                //    {
                //        new ColumnDefinition { Width = GridLength.Star }
                //    }
                //};
                foreach (var item in consumers)
                {
                    //AddRowConsumers(item.accountNo, item.meterno, item.consumer_name, item.status,item.seqno,item.address);
                    string AutoID;
                    if (item.status == 0)
                    {
                        AutoID = $"UInnerGrid_{item.accountID}";

                        tmpgridConsumerUnbilled = new Grid();
                        tmpgridConsumerUnbilled.AutomationId = AutoID;
                        tmpgridConsumerUnbilled.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                        tmpgridConsumerUnbilled.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                        tmpgridConsumerUnbilled.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                        tmpgridConsumerUnbilled.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
                        tmpgridConsumerUnbilled.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        ConsumerRecords.AutoID_UIndex.Add(AutoID + "," + rowindex_unbilled);
                        
                    }
                    else
                    {
                        AutoID = $"BInnerGrid_{item.accountID}";

                        tmpgridConsumerBilled = new Grid();
                        tmpgridConsumerBilled.AutomationId = AutoID;
                        tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                        tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                        tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                        tmpgridConsumerBilled.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
                        tmpgridConsumerBilled.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        ConsumerRecords.AutoID_BIndex.Add(AutoID + "," + rowindex_billed);
                    }
                         
                    AddRowConsumers_new(item.accountNo, item.meterno, item.consumer_name, item.status, item.seqno, item.address,item.accountID);                    
                    
                    if (item.status == 0)
                    {
                        gridConsumersUnbilled.Add(tmpgridConsumerUnbilled);
                        rowindex_unbilled++;
                        //if (rowindex_unbilled % 10 == 0)
                        //{
                        //    //Add the 20 data collection grid to list
                        //    gridConsumersUnbilled.Add(tmpgridConsumerUnbilled);
                        //    batchno_u++;
                        //    tmpgridConsumerUnbilled = new Grid
                        //    {
                        //        AutomationId = $"UGrid_{batchno_u}",
                        //        RowDefinitions =
                        //    {
                        //        new RowDefinition { Height = GridLength.Auto }
                        //    },
                        //        ColumnDefinitions =
                        //    {
                        //        new ColumnDefinition { Width = GridLength.Star }
                        //    }

                        //    };
                        //}
                    }
                    if (item.status == 1)
                    {
                        gridConsumersBilled.Add(tmpgridConsumerBilled);
                        rowindex_billed++;
                        //if (rowindex_billed % 10 == 0)
                        //{
                        //    //Add the 20 data collection grid to list
                        //    gridConsumersBilled.Add(tmpgridConsumerBilled);
                        //    batchno_b++;
                        //    tmpgridConsumerBilled = new Grid
                        //    {
                        //        AutomationId = $"BGrid_{batchno_b}",
                        //        RowDefinitions =
                        //    {
                        //        new RowDefinition { Height = GridLength.Auto }
                        //    },
                        //        ColumnDefinitions =
                        //    {
                        //        new ColumnDefinition { Width = GridLength.Star }
                        //    }

                        //    };
                        //}
                    }
                    

                }
                //if (tmpgridConsumerUnbilled.Children.Count > 0)
                //{
                //    gridConsumersUnbilled.Add(tmpgridConsumerUnbilled);
                //}
                //if (tmpgridConsumerBilled.Children.Count > 0)
                //{
                //    gridConsumersBilled.Add(tmpgridConsumerBilled);
                //}

                //Pass the generated grid to class
                ConsumerRecords.gridConsumerUnbilledCollection = gridConsumersUnbilled;
                if (gridConsumersBilled.Count > 0)
                {
                    ConsumerRecords.gridConsumerBilledCollection = gridConsumersBilled;
                }
                
                ConsumerRecords.noofrecordsBilled = rowindex_billed;
            });
            return isDone;
        }
        private void AddRowConsumers(string acctno, string meterno, string consumer_name, int status, int bindID,string consumer_address)
        {
            FrameTappedCommand = new Command<string>(OnFrameTapped);

            if (status == 0)
            {
                tmpgridConsumerUnbilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            else
            {
                tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            int uframeHeight = 130;
            int lblfontsize = 16;
            int valuefontsize = 18;
            int meterlbl_y = 60;
            int metervalue_y = 80;

            int namevalue_font = 16;
            int namevalue_y = 0;
            int nameicon_y = 10;
            int namevalue_w = 140;

            int addressvalue_font = 14;
            int addressvalue_y = 50;
            int addressicon_y = 60;
            int addressvalue_w = 170;
            int addressmaxline = 3;

            int statframe_y = 5;
            int statlbl_y = 7;


            if (MyDeviceInfo.device_heightpix < 2300 && MyDeviceInfo.device_widthpix > 900)
            {
                uframeHeight = 100;
                lblfontsize = 12;
                valuefontsize = 16;
                meterlbl_y = 55;
                metervalue_y = 75;

                nameicon_y = 15;
                namevalue_font = 14;
                namevalue_y = 5;
                namevalue_w = 140;

                addressvalue_font = 12;
                addressvalue_y = 50;
                addressvalue_w = 170;

                statframe_y = 15;
                statlbl_y = 17;
            }
            else if (MyDeviceInfo.device_heightpix < 900 && MyDeviceInfo.device_widthpix < 500)
            {
                addressvalue_w = 120;
                addressmaxline = 2;
                namevalue_w = 120;
            }


            var lowerFrame = new Frame
            {
                BackgroundColor = Colors.LightGray,
                AutomationId = $"Lframe_{bindID}",
                CornerRadius = 5,
                Margin = new Thickness(5, 15, 5, 5)
            };
            var upperFrame = new Frame
            {
                BackgroundColor = Colors.White,
                AutomationId = $"Uframe_{bindID}",
                HeightRequest = uframeHeight,
                CornerRadius = 5,
                Margin = new Thickness(10)
            };

            var tapGestureRecognizer = new TapGestureRecognizer();
            tapGestureRecognizer.Command = FrameTappedCommand;
            tapGestureRecognizer.CommandParameter = acctno;
            upperFrame.GestureRecognizers.Add(tapGestureRecognizer);


            var absoluteLayout = new AbsoluteLayout
            {
                VerticalOptions = LayoutOptions.Center,
                AutomationId = $"AbsoluteLayout_{bindID}",
            };
            var acctNoLabel = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = "Account No:",
                TextColor = Colors.Black,
                FontSize = lblfontsize,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start
            };
            var acctNoValue = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = acctno,
                TextColor = Colors.Black,
                FontSize = valuefontsize,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start
            };
            var meternoLabel = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = "Meter No:",
                TextColor = Colors.Black,
                FontSize = lblfontsize, //16
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start
            };
            var meternoValue = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = meterno,
                TextColor = Colors.Black,
                FontSize = valuefontsize,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start
            };
            //var nameLabel = new Label
            //{
            //    FontAttributes = FontAttributes.Bold,
            //    Text = "Name:",
            //    TextColor = Colors.Black,
            //    FontSize = 16,
            //    VerticalOptions = LayoutOptions.Center,
            //    HorizontalOptions = LayoutOptions.Start
            //};
            var imageLabel = new Image
            {
                Source = "menuicons/person.png",
                HeightRequest = 25,
                WidthRequest = 25
            };
            var nameValue = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = consumer_name,
                TextColor = Colors.Black,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 2,
                FontSize = namevalue_font,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start
            };
            var imageAddressLabel = new Image
            {
                Source = "menuicons/address.png",
                HeightRequest = 25,
                WidthRequest = 25
            };
            var nameAddressValue = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = consumer_address,
                TextColor = Colors.Black,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = addressmaxline,
                FontSize = addressvalue_font,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start
            };
            var consumerColor = Colors.Green;
            string consumerTag = "C";
            if (status == 0)
            {
                consumerColor = Colors.Red;
                consumerTag = "U";
            }
            else if (status == 1)
            {
                consumerColor = Colors.Blue;
                consumerTag = "B";
            }
            var circleFrame = new Frame
            {
                BackgroundColor = consumerColor,
                HeightRequest = 30,
                WidthRequest = 30,
                CornerRadius = 75,
                Margin = new Thickness(10)
            };
            var circleValue = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = consumerTag,
                TextColor = Colors.White,
                FontSize = 18
            };

            var boundsAcctLabel = new Rect(20, 10, 100, 20);
            AbsoluteLayout.SetLayoutBounds(acctNoLabel, boundsAcctLabel);
            var boundsAcctValue = new Rect(20, 30, 100, 20);
            AbsoluteLayout.SetLayoutBounds(acctNoValue, boundsAcctValue);

            var boundsMeterLabel = new Rect(20, meterlbl_y, 100, 20);
            AbsoluteLayout.SetLayoutBounds(meternoLabel, boundsMeterLabel);
            var boundsMeterValue = new Rect(20, metervalue_y, 100, 20);
            AbsoluteLayout.SetLayoutBounds(meternoValue, boundsMeterValue);

            var boundsNameLabel = new Rect(140, nameicon_y, 25, 25);
            AbsoluteLayout.SetLayoutBounds(imageLabel, boundsNameLabel);
            var boundsNameValue = new Rect(175, namevalue_y, namevalue_w, 50);
            AbsoluteLayout.SetLayoutBounds(nameValue, boundsNameValue);

            var boundsAddressLabel = new Rect(140, 60, 25, 25);
            AbsoluteLayout.SetLayoutBounds(imageAddressLabel, boundsAddressLabel);
            var boundsAddressValue = new Rect(175, addressvalue_y, addressvalue_w, 60);
            AbsoluteLayout.SetLayoutBounds(nameAddressValue, boundsAddressValue);

            var boundsCircle = new Rect(340, statframe_y, 30, 30);
            AbsoluteLayout.SetLayoutBounds(circleFrame, boundsCircle);
            var boundsCircleValue = new Rect(349, statlbl_y, 30, 30);
            AbsoluteLayout.SetLayoutBounds(circleValue, boundsCircleValue);

            absoluteLayout.Add(acctNoLabel);
            absoluteLayout.Add(acctNoValue);
            absoluteLayout.Add(meternoLabel);
            absoluteLayout.Add(meternoValue);
            absoluteLayout.Add(imageLabel);
            absoluteLayout.Add(nameValue);
            absoluteLayout.Add(imageAddressLabel);
            absoluteLayout.Add(nameAddressValue);
            absoluteLayout.Add(circleFrame);
            absoluteLayout.Add(circleValue);

            if (status == 0)
            {
                tmpgridConsumerUnbilled.Add(lowerFrame, 0, rowindex_unbilled);
                tmpgridConsumerUnbilled.Add(upperFrame, 0, rowindex_unbilled);
                tmpgridConsumerUnbilled.Add(absoluteLayout, 0, rowindex_unbilled);
            }
            else
            {
                tmpgridConsumerBilled.Add(lowerFrame, 0, rowindex_billed);
                tmpgridConsumerBilled.Add(upperFrame, 0, rowindex_billed);
                tmpgridConsumerBilled.Add(absoluteLayout, 0, rowindex_billed);
            }
            
        }

        private void AddRowConsumers_new(string acctno, string meterno, string consumer_name, int status, long bindID, string consumer_address,int acctID)
        {
            FrameTappedCommand = new Command<string>(OnFrameTapped);
            //string AutoID;
            //if (status == 0)
            //{
            //    //tmpgridConsumerUnbilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            //    AutoID = $"UInnerGrid_{bindID}";
            //}
            //else
            //{
            //    //tmpgridConsumerBilled.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            //    AutoID = $"BInnerGrid_{bindID}";
            //}

           
            int lblfontsize = 16;
            int valuefontsize = 18;          
            int namevalue_font = 16;
            int gridcolumn_width = 135;
            int addressvalue_font = 14;    

            if (MyDeviceInfo.device_heightpix < 2300 && MyDeviceInfo.device_widthpix > 900)
            {
                
                lblfontsize = 12;
                valuefontsize = 16;
             
                namevalue_font = 14;
              

                addressvalue_font = 12;             

                
            }
            else if (MyDeviceInfo.device_heightpix < 900 && MyDeviceInfo.device_widthpix < 500)
            {
                namevalue_font = 12;
                valuefontsize = 14;
                addressvalue_font = 12;
            }

            var lowerFrame = new Frame
            {
                BackgroundColor = Colors.LightGray,
                AutomationId = $"Lframe_{bindID}",
                CornerRadius = 5,
                Margin = new Thickness(5, 15, 5, 5)
            };
            var upperFrame = new Frame
            {
                BackgroundColor = Colors.White,
                AutomationId = $"Uframe_{bindID}",                
                CornerRadius = 5,
                Margin = new Thickness(10)
            };
            var frontframe = new Frame
            {
                BackgroundColor = Colors.Transparent,
                AutomationId = $"Fframe_{bindID}",
                CornerRadius = 5,
                Margin = new Thickness(10)
            };
            var acctNoLabel = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = "Account No:",
                TextColor = Colors.Black,
                FontSize = lblfontsize                
            };
            var acctNoValue = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = acctno, //change to acctno
                TextColor = Colors.Black,
                FontSize = valuefontsize
               
            };
            var meternoLabel = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = "Meter No:",
                TextColor = Colors.Black,
                FontSize = lblfontsize
               
            };
            var meternoValue = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = meterno,
                TextColor = Colors.Black,
                FontSize = valuefontsize
               
            };
            var imageLabel = new Image
            {
                Source = "menuicons/person.png",
                HeightRequest = 25,
                WidthRequest = 25
                
            };
            var nameValue = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = consumer_name,
                TextColor = Colors.Black,               
                MaxLines = 2,
                FontSize = namevalue_font
               
            };
            var imageAddressLabel = new Image
            {
                Source = "menuicons/address.png",
                HeightRequest = 25,
                WidthRequest = 25
            };
            var nameAddressValue = new Label
            {
                FontAttributes = FontAttributes.Bold,
                Text = consumer_address,
                TextColor = Colors.Black,                
                MaxLines = 3,
                FontSize = addressvalue_font
                
            };
            BoxView tmpBox = new BoxView
            {
                BackgroundColor = Colors.Transparent,
                HeightRequest = 20
            };

            var tapGestureRecognizer = new TapGestureRecognizer();
            tapGestureRecognizer.Command = FrameTappedCommand;
            tapGestureRecognizer.CommandParameter = acctno;
            frontframe.GestureRecognizers.Add(tapGestureRecognizer);

            //maingrid            

            //Grid itemGrid = new Grid();
            //itemGrid.AutomationId = AutoID;
            //itemGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            //itemGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            //itemGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            //itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gridcolumn_width) });
            //itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });



            //Acct Grid
            Grid itemGrid_1_1 = new Grid();
            itemGrid_1_1.Margin = new Thickness(20, 15, 10, 0);

            itemGrid_1_1.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            itemGrid_1_1.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            itemGrid_1_1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gridcolumn_width) });
            itemGrid_1_1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            itemGrid_1_1.Add(acctNoLabel, 0, 0);
            itemGrid_1_1.Add(acctNoValue, 0, 1);

            //Name Grid
            Grid itemGrid_1_2 = new Grid();
            itemGrid_1_2.Margin = new Thickness(0, 10, 20, 0);

            itemGrid_1_2.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            itemGrid_1_2.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            itemGrid_1_2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            itemGrid_1_2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid itemGrid_1_2_1 = new Grid();
            //itemGrid_1_2_1.Margin = new Thickness(20, 15, 10, 0);

            itemGrid_1_2_1.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            itemGrid_1_2_1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            nameValue.Margin = new Thickness(5, 5, 0, 0);
            itemGrid_1_2_1.Add(nameValue, 0, 0);

            //Name grid Add
            imageLabel.Margin = new Thickness(0, 10, 5, 0);
            itemGrid_1_2.Add(imageLabel, 0, 0);
            Grid.SetRowSpan(imageLabel, 2);
            itemGrid_1_2.Add(itemGrid_1_2_1, 1, 0);



            //Meter Grid
            Grid itemGrid_2_1 = new Grid();
            itemGrid_2_1.Margin = new Thickness(20, 0, 10, 0);

            itemGrid_2_1.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            itemGrid_2_1.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            itemGrid_2_1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gridcolumn_width) });
            itemGrid_2_1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            itemGrid_2_1.Add(meternoLabel, 0, 0);
            itemGrid_2_1.Add(meternoValue, 0, 1);

            //Address Grid
            Grid itemGrid_2_2 = new Grid();
            itemGrid_2_2.Margin = new Thickness(0, 0, 20, 0);

            itemGrid_2_2.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            itemGrid_2_2.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            itemGrid_2_2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            itemGrid_2_2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid itemGrid_2_2_1 = new Grid();
            //itemGrid_2_2_1.Margin = new Thickness(20, 15, 10, 0);

            itemGrid_2_2_1.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            itemGrid_2_2_1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            nameAddressValue.Margin = new Thickness(5, 5, 0, 0);
            itemGrid_2_2_1.Add(nameAddressValue, 0, 0);

            imageAddressLabel.Margin = new Thickness(0, 10, 5, 0);
            itemGrid_2_2.Add(imageAddressLabel, 0, 0);
            Grid.SetRowSpan(imageAddressLabel, 2);
            itemGrid_2_2.Add(itemGrid_2_2_1, 1, 0);

            //tmpgridConsumerUnbilled.Add(lowerFrame, 0, 0);
            //Grid.SetRowSpan(lowerFrame, 3);
            //Grid.SetColumnSpan(lowerFrame, 2);

            //tmpgridConsumerUnbilled.Add(upperFrame, 0, 0);
            //Grid.SetRowSpan(upperFrame, 3);
            //Grid.SetColumnSpan(upperFrame, 2);

            //tmpgridConsumerUnbilled.Add(itemGrid_1_1, 0, 0);
            //tmpgridConsumerUnbilled.Add(itemGrid_1_2, 1, 0);

            //tmpgridConsumerUnbilled.Add(itemGrid_2_1, 0, 1);
            //tmpgridConsumerUnbilled.Add(itemGrid_2_2, 1, 1);

            //tmpgridConsumerUnbilled.Add(tmpBox, 0, 2);
            //Grid.SetColumnSpan(tmpBox, 2);

            if (status == 0)
            {
                tmpgridConsumerUnbilled.Add(lowerFrame, 0, 0);
                Grid.SetRowSpan(lowerFrame, 3);
                Grid.SetColumnSpan(lowerFrame, 2);

                tmpgridConsumerUnbilled.Add(upperFrame, 0, 0);
                Grid.SetRowSpan(upperFrame, 3);
                Grid.SetColumnSpan(upperFrame, 2);

                tmpgridConsumerUnbilled.Add(itemGrid_1_1, 0, 0);
                tmpgridConsumerUnbilled.Add(itemGrid_1_2, 1, 0);

                tmpgridConsumerUnbilled.Add(itemGrid_2_1, 0, 1);
                tmpgridConsumerUnbilled.Add(itemGrid_2_2, 1, 1);

                tmpgridConsumerUnbilled.Add(tmpBox, 0, 2);
                Grid.SetColumnSpan(tmpBox, 2);

                tmpgridConsumerUnbilled.Add(frontframe, 0, 0);
                Grid.SetRowSpan(frontframe, 3);
                Grid.SetColumnSpan(frontframe, 2);
            }
            else
            {
                tmpgridConsumerBilled.Add(lowerFrame, 0, 0);
                Grid.SetRowSpan(lowerFrame, 3);
                Grid.SetColumnSpan(lowerFrame, 2);

                tmpgridConsumerBilled.Add(upperFrame, 0, 0);
                Grid.SetRowSpan(upperFrame, 3);
                Grid.SetColumnSpan(upperFrame, 2);

                tmpgridConsumerBilled.Add(itemGrid_1_1, 0, 0);
                tmpgridConsumerBilled.Add(itemGrid_1_2, 1, 0);

                tmpgridConsumerBilled.Add(itemGrid_2_1, 0, 1);
                tmpgridConsumerBilled.Add(itemGrid_2_2, 1, 1);

                tmpgridConsumerBilled.Add(tmpBox, 0, 2);
                Grid.SetColumnSpan(tmpBox, 2);

                tmpgridConsumerBilled.Add(frontframe, 0, 0);
                Grid.SetRowSpan(frontframe, 3);
                Grid.SetColumnSpan(frontframe, 2);
            }

        }
    }
}
